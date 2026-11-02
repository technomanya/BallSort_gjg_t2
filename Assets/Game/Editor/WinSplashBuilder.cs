using BallSortGame.Gameplay;
using BallSortGame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// Adds a WinSplash (dim, "Mana Bloom" logo, Hyper Casual FX fireworks) to the open scene.
    /// </summary>
    public static class WinSplashBuilder
    {
        const string Name = "WinSplash";
        const string FxDir = "Assets/Lana Studio/Hyper Casual FX/Prefabs/";
        static readonly string[] Fireworks =
        {
            FxDir + "Confetti/Confetti_blast_multicolor.prefab",
            FxDir + "Flash/Flash_star_ellow_purple.prefab",
            FxDir + "Confetti/Confetti_blast_multicolor.prefab",
        };

        [MenuItem("BallSort/Build Win Splash")]
        public static void Build()
        {
            var skin = AssetDatabase.LoadAssetAtPath<UISkin>("Assets/Game/Resources/UISkin.asset");
            if (skin == null)
            {
                Debug.LogWarning("Build Win Splash: Resources/UISkin.asset is missing (run BallSort > Rebuild UI Skin first).");
                return;
            }
            var old = GameObject.Find(Name);
            if (old == null)
            {
                var existing = Object.FindFirstObjectByType<WinSplash>(FindObjectsInactive.Include);
                if (existing != null) old = existing.gameObject;
            }
            // the splash may be hand-tuned, so ask before replacing it and never save the scene
            if (old != null && !EditorUtility.DisplayDialog("Build Win Splash",
                    "The scene already has a WinSplash. Replace it? Any hand tuning of it is lost (Undo brings it back).", "Replace", "Cancel")) return;
            if (old != null) Undo.DestroyObjectImmediate(old);

            var go = new GameObject(Name);
            Undo.RegisterCreatedObjectUndo(go, "Build Win Splash");
            var splash = go.AddComponent<WinSplash>();
            splash.game = Object.FindFirstObjectByType<GameManager>();
            splash.cam = Camera.main;

            splash.dim = Sprite("dim", go.transform, skin.overlay, splash.SortingOrder);
            splash.dim.color = splash.DimColor;
            splash.logo = Sprite("logo", go.transform, skin.logo, splash.SortingOrder + 1);

            var systems = new System.Collections.Generic.List<ParticleSystem>();
            for (int i = 0; i < Fireworks.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Fireworks[i]);
                if (prefab == null)
                {
                    Debug.LogWarning("Build Win Splash: missing " + Fireworks[i] + " (import Hyper Casual FX from the Package Manager's My Assets).");
                    continue;
                }
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
                fx.name = "firework_" + i;
                var ps = fx.GetComponent<ParticleSystem>();
                if (ps == null) ps = fx.GetComponentInChildren<ParticleSystem>(true);
                foreach (var child in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = child.main;
                    main.playOnAwake = false;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
                foreach (var r in fx.GetComponentsInChildren<ParticleSystemRenderer>(true)) r.sortingOrder = splash.SortingOrder + 2 + Mathf.Max(0, r.sortingOrder % 10);
                fx.SetActive(false);
                if (ps != null) systems.Add(ps);
            }
            splash.fireworks = systems.ToArray();
            splash.dim.gameObject.SetActive(false);
            splash.logo.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("Build Win Splash: WinSplash added with " + systems.Count + " fireworks (scene not saved).");
        }

        static SpriteRenderer Sprite(string name, Transform parent, Sprite sprite, int order)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }
    }
}
