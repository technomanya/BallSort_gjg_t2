using BallSortGame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// Adds the main menu to the game canvas of the open scene, after the mockup: the board shows through a dim
    /// overlay, the logo sits in the middle and a tap anywhere starts the level. The coin balance keeps a pill
    /// top left (LevelCoinsView copies it for the win screen); the MINI GAME button sits under the tap hint.
    /// Menu: BallSort > Add Main Menu (also run by Rebuild HUD).
    /// </summary>
    public static class MainMenuBuilder
    {
        const string Name = "MainMenuView";
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        [MenuItem("BallSort/Add Main Menu")]
        public static void Add()
        {
            var hud = Object.FindFirstObjectByType<GameHUD>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("Add Main Menu: no GameHUD in the open scene.");
                return;
            }
            if (hud.mainMenu != null || hud.transform.Find(Name) != null)
            {
                Debug.Log("Add Main Menu: the scene already has one; edit it under " + hud.name + "/" + Name + ".");
                return;
            }
            var skin = AssetDatabase.LoadAssetAtPath<UISkin>("Assets/Game/Resources/UISkin.asset");
            if (skin == null) skin = NewUISkinBuilder.BuildSkin();

            // full-screen dim over the HUD; it is also the START button
            var root = new GameObject(Name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(MainMenuView));
            Undo.RegisterCreatedObjectUndo(root, "Add Main Menu");
            var rt = (RectTransform)root.transform;
            rt.SetParent(hud.transform, false);
            rt.SetAsLastSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            root.GetComponent<Image>().color = BallSortSceneBuilder.Dim;
            var start = root.GetComponent<Button>();
            start.transition = Selectable.Transition.None;
            var menu = root.GetComponent<MainMenuView>();
            menu.game = hud.game;
            menu.startButton = start;

            menu.logo = BallSortSceneBuilder.Decor("logo", rt, skin.logo, new Vector2(0f, 80f), 760f).rectTransform;

            var hint = BallSortSceneBuilder.Title("tap_hint", rt, new Vector2(0f, -520f), new Vector2(700f, 90f), "TAP TO PLAY", 52);
            menu.pulse = hint.rectTransform;

            // level pill above the hint (the HUD's own pill is hidden while the menu is up)
            var levelBg = BallSortSceneBuilder.MakeImage("level_pill", rt, Center, Center, Center, new Vector2(0f, -380f), new Vector2(400f, 150f), skin.buttonPill != null ? skin.buttonPill : skin.buttonRect, Color.white, Image.Type.Sliced);
            levelBg.raycastTarget = false;
            menu.levelText = BallSortSceneBuilder.MakeText("text", levelBg.rectTransform, Vector2.zero, Vector2.one, Center, new Vector2(0f, 2f), new Vector2(-24f, 0f), "LEVEL 1", 54, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            BallSortSceneBuilder.AddShadow(menu.levelText.gameObject);
            BallSortSceneBuilder.LevelLabel(menu.levelText, skin.wordLevel);

            // the game view is closed while the menu is up: HUD bars off (the board is hidden by MainMenuView itself)
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (var part in new[] { "top_bar", "booster_bar" })   // popups and the toast manage themselves
            {
                var t = hud.transform.Find(part);
                if (t != null) hidden.Add(t.gameObject);
            }
            menu.hideWhileOpen = hidden.ToArray();

            // coin balance pill, top left
            var pill = BallSortSceneBuilder.MakeImage("coins", rt, new Vector2(0f, 1f), new Vector2(0f, 1f), Center, new Vector2(200f, -133f), new Vector2(330f, 110f), skin.buttonPill != null ? skin.buttonPill : skin.pill, Color.white, Image.Type.Sliced);
            pill.raycastTarget = false;
            BallSortSceneBuilder.Decor("coin", pill.transform, skin.coin, new Vector2(-105f, 0f), 84f);
            menu.coinsText = BallSortSceneBuilder.MakeText("text", pill.transform, Vector2.zero, Vector2.one, Center, new Vector2(40f, 2f), new Vector2(-80f, 0f), "0", 52, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            BallSortSceneBuilder.AddShadow(menu.coinsText.gameObject);
            menu.miniGameButton = MiniGameButton(rt, skin);

            Undo.RecordObject(hud, "Add Main Menu");
            hud.mainMenu = menu;
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            Debug.Log("Add Main Menu: added " + hud.name + "/" + Name + "; save the scene to keep it.");
        }

        /// <summary>Green "MINI GAME" button with a coin, under the tap hint; it catches its own taps, so START does not fire.</summary>
        internal static Button MiniGameButton(RectTransform menuRoot, UISkin skin)
        {
            var btn = BallSortSceneBuilder.PillButton("btn - mini_game", menuRoot, new Vector2(0f, -720f), new Vector2(560f, 150f), skin.buttonRectGreen != null ? skin.buttonRectGreen : skin.buttonPill, "MINI GAME", 54);
            var label = (RectTransform)btn.transform.Find("label");
            label.offsetMin = new Vector2(110f, label.offsetMin.y);   // room for the coin on the left
            BallSortSceneBuilder.Decor("coin", btn.transform, skin.coin, new Vector2(-190f, 4f), 96f).raycastTarget = false;
            return btn;
        }
    }
}
