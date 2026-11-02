using System.Linq;
using BallSortGame.Gameplay;
using BallSortGame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// Adds rising stars behind the board and over the main menu backdrop. Skips any that already exist.
    /// </summary>
    public static class BackgroundStarsBuilder
    {
        const string Dir = "Assets/Game/Art/Backgrounds/";
        const string WorldName = "Background Stars";
        const string MenuName = "stars";

        [MenuItem("BallSort/Add Background Stars")]
        public static void Add()
        {
            var sprites = new[] { Load("Background star sharp.png"), Load("Background star blured.png") }.Where(s => s != null).ToArray();
            if (sprites.Length == 0)
            {
                Debug.LogWarning("Add Background Stars: no star sprites in " + Dir);
                return;
            }
            var scene = EditorSceneManager.GetActiveScene();
            int added = 0;

            if (GameObject.Find(WorldName) == null)
            {
                var go = new GameObject(WorldName, typeof(RisingStars));
                Undo.RegisterCreatedObjectUndo(go, "Add Background Stars");
                var stars = go.GetComponent<RisingStars>();
                stars.sprites = sprites;
                stars.cam = Camera.main;
                var bg = GameObject.Find("Background");
                if (bg != null)
                {
                    go.transform.SetSiblingIndex(bg.transform.GetSiblingIndex() + 1);
                    if (bg.TryGetComponent(out SpriteRenderer bgSr)) stars.sortingOrder = bgSr.sortingOrder + 1;
                    stars.z = bg.transform.position.z - 1f;
                }
                added++;
            }

            var menu = Object.FindFirstObjectByType<MainMenuView>(FindObjectsInactive.Include);
            if (menu != null && menu.transform.Find(MenuName) == null)
            {
                var go = new GameObject(MenuName, typeof(RectTransform), typeof(RisingStars));
                Undo.RegisterCreatedObjectUndo(go, "Add Background Stars");
                var rt = (RectTransform)go.transform;
                rt.SetParent(menu.transform, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                var bg = menu.transform.Find("bg");
                rt.SetSiblingIndex(bg != null ? bg.GetSiblingIndex() + 1 : 0);
                go.GetComponent<RisingStars>().sprites = sprites;
                added++;
            }

            if (added > 0) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Add Background Stars: added {added}; save the scene to keep them.");
        }

        static Sprite Load(string file) => AssetDatabase.LoadAllAssetsAtPath(Dir + file).OfType<Sprite>().FirstOrDefault();
    }
}
