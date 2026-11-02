using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// For development builds only: turns Burst AOT off (the game has no Burst jobs, it only slows the build) and sets
    /// IL2CPP to faster builds. Both are restored after the build, pass or fail.
    /// </summary>
    class DevBuildSpeedups : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string KeyActive = "BallSort.DevBuildSpeedups.active";
        const string KeyTarget = "BallSort.DevBuildSpeedups.target";
        const string KeyCodeGen = "BallSort.DevBuildSpeedups.codegen";
        const string KeyBurst = "BallSort.DevBuildSpeedups.burst";

        // before Burst's own preprocessor (order 0), which reads the AOT settings file
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
            // fires once BuildPlayer returns, also after a failed build
            EditorApplication.delayCall += Restore;
            Debug.Log($"Development build: Burst AOT {(burst == true ? "switched off" : "already off")}, IL2CPP code generation set to faster builds (was {codeGen}). Both are restored after the build.");
        }

        public void OnPostprocessBuild(BuildReport report) => Restore();

        // a crashed build never reaches the postprocessor, so restore on the next load
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

        // Sets Burst's per-platform "Enable Burst Compilation" through reflection (the settings class is internal).
        // Returns the previous value, or null when Burst is not installed.
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
