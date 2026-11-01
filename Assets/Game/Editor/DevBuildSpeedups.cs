using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// Makes development builds compile faster. While a Development Build runs, Burst AOT compilation is switched off
    /// (the game has no Burst jobs of its own; Burst only spends 15-20 minutes per CPU architecture on package code)
    /// and IL2CPP code generation is set to "Faster (smaller) builds" (full generic sharing: far less C++ to compile,
    /// a slower player, which does not matter for testing). Both settings are put back right after the build, whether
    /// it succeeded or failed, so release builds are untouched. Nothing happens for a non-development build.
    /// </summary>
    class DevBuildSpeedups : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string KeyActive = "BallSort.DevBuildSpeedups.active";
        const string KeyTarget = "BallSort.DevBuildSpeedups.target";
        const string KeyCodeGen = "BallSort.DevBuildSpeedups.codegen";
        const string KeyBurst = "BallSort.DevBuildSpeedups.burst";

        /// <summary>Runs before Burst's own build preprocessor (order 0), which reads the AOT settings file.</summary>
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if ((report.summary.options & BuildOptions.Development) == 0) return;
            if (EditorPrefs.GetBool(KeyActive, false)) Restore();   // a previous build left its changes behind

            var target = report.summary.platform;
            var named = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
            var codeGen = PlayerSettings.GetIl2CppCodeGeneration(named);
            PlayerSettings.SetIl2CppCodeGeneration(named, Il2CppCodeGeneration.OptimizeSize);
            bool? burst = SetBurstAot(target, false);

            EditorPrefs.SetBool(KeyActive, true);
            EditorPrefs.SetInt(KeyTarget, (int)target);
            EditorPrefs.SetInt(KeyCodeGen, (int)codeGen);
            EditorPrefs.SetInt(KeyBurst, burst.HasValue ? (burst.Value ? 1 : 0) : -1);
            // the build runs synchronously: this fires once BuildPlayer has returned, also when the build failed
            EditorApplication.delayCall += Restore;
            Debug.Log($"Development build: Burst AOT {(burst == true ? "switched off" : "already off")}, IL2CPP code generation set to faster builds (was {codeGen}). Both are restored after the build.");
        }

        public void OnPostprocessBuild(BuildReport report) => Restore();

        /// <summary>A crashed or aborted build never reaches the postprocessor: put the settings back on the next load.</summary>
        [InitializeOnLoadMethod]
        static void RestoreLeftover()
        {
            if (EditorPrefs.GetBool(KeyActive, false)) Restore();
        }

        static void Restore()
        {
            if (!EditorPrefs.GetBool(KeyActive, false)) return;
            EditorPrefs.SetBool(KeyActive, false);
            var target = (BuildTarget)EditorPrefs.GetInt(KeyTarget, (int)BuildTarget.Android);
            var named = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
            var codeGen = (Il2CppCodeGeneration)EditorPrefs.GetInt(KeyCodeGen, (int)Il2CppCodeGeneration.OptimizeSpeed);
            PlayerSettings.SetIl2CppCodeGeneration(named, codeGen);
            int burst = EditorPrefs.GetInt(KeyBurst, -1);
            if (burst >= 0) SetBurstAot(target, burst == 1);
            AssetDatabase.SaveAssets();
            Debug.Log($"Development build done: IL2CPP code generation back to {codeGen}{(burst == 1 ? ", Burst AOT back on" : "")}.");
        }

        /// <summary>
        /// Sets Burst's per-platform "Enable Burst Compilation" (Project Settings > Burst AOT Settings), which Burst
        /// keeps in ProjectSettings/BurstAotSettings_&lt;Target&gt;.json and reads at build time. The class is internal,
        /// so this goes through reflection. Returns the previous value, or null when Burst is not installed.
        /// </summary>
        static bool? SetBurstAot(BuildTarget target, bool enabled)
        {
            try
            {
                var type = Type.GetType("Unity.Burst.Editor.BurstPlatformAotSettings, Unity.Burst.Editor");
                if (type == null) return null;
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var get = type.GetMethod("GetOrCreateSettings", any, null, new[] { typeof(BuildTarget?) }, null);
                var save = type.GetMethod("Save", any, null, new[] { typeof(BuildTarget?) }, null);
                var field = type.GetField("EnableBurstCompilation", any);
                if (get == null || save == null || field == null) return null;
                var args = new object[] { (BuildTarget?)target };
                var settings = get.Invoke(null, args);
                bool previous = (bool)field.GetValue(settings);
                if (previous != enabled)
                {
                    field.SetValue(settings, enabled);
                    save.Invoke(settings, args);
                }
                return previous;
            }
            catch (Exception e)
            {
                Debug.LogWarning("DevBuildSpeedups: could not change the Burst AOT setting: " + e.Message);
                return null;
            }
        }
    }
}
