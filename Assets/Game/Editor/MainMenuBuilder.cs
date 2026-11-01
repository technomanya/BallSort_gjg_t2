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
    /// top left (LevelCoinsView copies it for the win screen); the bottom tab bar (gold-framed panel) holds the
    /// Mana Flip, HOME and Mana Garden buttons.
    /// Menu: BallSort > Add Main Menu (also run by Rebuild HUD); BallSort > Rebuild Main Menu replaces the one in the scene.
    /// </summary>
    public static class MainMenuBuilder
    {
        const string Name = "MainMenuView";
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Color PlantGreen = new Color(0.25f, 0.85f, 0.45f, 1f);   // the garden's sprout tint (ManaGardenView.Green)

        /// <summary>Throws the scene's main menu away and builds it again from this file (layout edits); saves the scene.</summary>
        [MenuItem("BallSort/Rebuild Main Menu")]
        public static void Rebuild()
        {
            var hud = Object.FindFirstObjectByType<GameHUD>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("Rebuild Main Menu: no GameHUD in the open scene.");
                return;
            }
            var old = hud.mainMenu != null ? hud.mainMenu.transform : hud.transform.Find(Name);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            hud.mainMenu = null;
            Add();
            BallSortSceneBuilder.ApplyWordArt(hud, NewUISkinBuilder.BuildSkin());
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Rebuild Main Menu: main menu rebuilt and the scene saved.");
        }

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
            NavBar(rt, skin, menu);

            Undo.RecordObject(hud, "Add Main Menu");
            hud.mainMenu = menu;
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            Debug.Log("Add Main Menu: added " + hud.name + "/" + Name + "; save the scene to keep it.");
        }

        /// <summary>
        /// Bottom tab bar on the gold-framed small panel (its lower frame sits under the screen edge like the booster bar):
        /// Mana Flip (crystal) on the left, HOME raised in the middle inside a gold ring (the current tab), Mana Garden
        /// (sprout) on the right. ManaGardenView adds its seed badge to the garden button at runtime.
        /// </summary>
        static void NavBar(RectTransform menuRoot, UISkin skin, MainMenuView menu)
        {
            var bar = BallSortSceneBuilder.MakeImage("nav_bar", menuRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Center, new Vector2(0f, 113f), new Vector2(900f, 346f), skin.pill, Color.white, Image.Type.Sliced);
            menu.miniGameButton = NavButton("btn - mana_flip", bar.transform, new Vector2(-280f, 30f), skin, skin.rewardCrystal, 150f, 112f, Color.white);
            menu.gardenButton = NavButton("btn - mana_garden", bar.transform, new Vector2(280f, 30f), skin, skin.iconPlant, 150f, 136f, PlantGreen);
            BallSortSceneBuilder.Decor("ring", bar.transform, skin.circleFrame, new Vector2(0f, 45f), 216f);
            menu.homeButton = NavButton("btn - home", bar.transform, new Vector2(0f, 45f), skin, skin.iconHome, 190f, 116f, Color.white);
        }

        /// <summary>Pink circle button of the kit with a tinted icon; it catches its own taps, so START does not fire.</summary>
        static Button NavButton(string name, Transform parent, Vector2 pos, UISkin skin, Sprite icon, float size, float iconSize, Color iconColor)
        {
            var btn = BallSortSceneBuilder.IconButton(name, parent, Center, pos, skin.buttonCircle, size, icon, iconSize, false);
            var img = btn.transform.Find("img - icon").GetComponent<Image>();
            img.color = iconColor;
            return btn;
        }
    }
}
