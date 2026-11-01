using System.Collections.Generic;
using System.IO;
using BallSortGame.Gameplay;
using BallSortGame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// Scene builder. <b>BallSort > Build Example Scene</b> configures the imported art, creates the Tube/Ball prefabs and
    /// builds the example scene from scratch. <b>BallSort > Rebuild HUD</b> only replaces the HUD canvas of the open
    /// scene (and the background), keeping the camera, GameManager and prefabs: that is the one to run after a UI change.
    ///
    /// Layout follows the UI mockups at the 1080x2340 reference resolution (mockup pixels x 1.617):
    /// top bar (restart button / gold LEVEL pill / settings button; MAIN MENU is on the settings page), the gold-framed booster bar at the
    /// bottom (undo / shuffle / add tube / little tube) with the 'i' info panel, the settings page (gold-framed panel: back button,
    /// haptic / music / sound), the "OUT OF MOVES" and "WELL DONE" popups (gold frames, stadium buttons inside the panel) and the
    /// main menu (logo over the dimmed board, tap to play).
    /// </summary>
    public static class BallSortSceneBuilder
    {
        const string Root = "Assets/Game";
        const string ArtUI = Root + "/Art/UI/";
        const string ScenePath = Root + "/Scenes/BallSortExample.unity";

        internal static readonly Color Gold = new Color(1f, 0.72f, 0.12f, 1f);         // popup titles
        internal static readonly Color Navy = new Color(0.12f, 0.06f, 0.30f, 1f);       // outlines, panel titles
        internal static readonly Color CamBackground = new Color(0.05f, 0.04f, 0.16f, 1f);
        internal static readonly Color Dim = new Color(0.03f, 0.02f, 0.10f, 0.82f);       // behind popups and the menu
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        [MenuItem("BallSort/Build Example Scene")]
        public static void Build()
        {
            ConfigureArt();
            var tubePrefab = BuildTubePrefab();
            var ballPrefab = BuildBallPrefab();
            BuildScene(tubePrefab, ballPrefab);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("BallSort example scene built: " + ScenePath);
        }

        /// <summary>Replaces the HUD canvas (and the main menu) of the open scene with the mockup layout; the rest of the scene stays.</summary>
        [MenuItem("BallSort/Rebuild HUD")]
        public static void RebuildHud()
        {
            var hud = Object.FindFirstObjectByType<GameHUD>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("Rebuild HUD: no GameHUD in the open scene.");
                return;
            }
            var gm = hud.game != null ? hud.game : Object.FindFirstObjectByType<GameManager>();
            var skin = NewUISkinBuilder.BuildSkin();
            for (int i = hud.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(hud.transform.GetChild(i).gameObject);
            hud.mainMenu = null;
            FillHud(hud, gm, skin);
            MainMenuBuilder.Add();
            ApplyWordArt(hud, skin);
            ApplyBackground(skin);
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Rebuild HUD: HUD rebuilt after the mockups and the scene saved.");
        }

        // ------------------------------------------------------------------ art import
        static void ConfigureArt()
        {
            var borders = new Dictionary<string, Vector4>
            {
                { "ui_round_rect_168", new Vector4(50, 50, 50, 50) },
                { "ui_button_green", new Vector4(20, 20, 20, 20) },
                { "ui_popup_bg", new Vector4(50, 50, 50, 50) },
                { "ui_popup_frame", new Vector4(60, 60, 60, 60) },
                { "ui_dark_pill", new Vector4(40, 40, 40, 40) },
                { "ui_level_pill", new Vector4(45, 45, 45, 45) },
            };
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(ArtUI + "NewUI/") || path.StartsWith(ArtUI + "Generated/")) continue;   // own sprite import; slices set by NewUISkinBuilder
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.spritePixelsPerUnit = 100;
                imp.mipmapEnabled = false;
                imp.filterMode = FilterMode.Bilinear;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.maxTextureSize = 2048;
                string name = Path.GetFileNameWithoutExtension(path);
                if (borders.TryGetValue(name, out var b)) imp.spriteBorder = b;
                // Tube.png is 512x1024 hi-res art: 200 PPU keeps the glass at the 4.8-unit tube height the ball slots expect
                if (name == "Tube") imp.spritePixelsPerUnit = 200;
                imp.SaveAndReimport();
            }
            AssetDatabase.Refresh();
        }

        static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        // ------------------------------------------------------------------ prefabs
        static Tube BuildTubePrefab()
        {
            var go = new GameObject("Tube", typeof(SpriteRenderer), typeof(BoxCollider2D), typeof(Tube));
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = LoadSprite(Root + "/Art/Tubes/Tube.png");
            sr.sortingOrder = 3;
            var col = go.GetComponent<BoxCollider2D>();
            col.size = new Vector2(2.11f, 6.4f);
            col.offset = new Vector2(0f, 0.8f);
            string path = Root + "/Prefabs/Tube.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<Tube>();
        }

        static Ball BuildBallPrefab()
        {
            var go = new GameObject("Ball", typeof(SpriteRenderer), typeof(Ball));
            var sr = go.GetComponent<SpriteRenderer>();
            sr.sprite = LoadSprite(Root + "/Art/Balls/ball_00.png");
            sr.sortingOrder = 2;
            string path = Root + "/Prefabs/Ball.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<Ball>();
        }

        // ------------------------------------------------------------------ scene
        static void BuildScene(Tube tubePrefab, Ball ballPrefab)
        {
            var skin = NewUISkinBuilder.BuildSkin();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // camera
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = CamBackground;
            camGo.transform.position = new Vector3(0f, 0f, -10f);

            // 2D global light so lit sprite materials render under the URP 2D renderer
            var lightGo = new GameObject("Global Light 2D", typeof(Light2D));
            var light = lightGo.GetComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;

            // background
            var bgGo = new GameObject("Background", typeof(SpriteRenderer), typeof(BackgroundFitter));
            var bgSr = bgGo.GetComponent<SpriteRenderer>();
            bgSr.sprite = skin.background != null ? skin.background : LoadSprite(Root + "/Art/Backgrounds/theme_default_v3.png");
            bgSr.sortingOrder = -10;
            bgGo.GetComponent<BackgroundFitter>().cam = cam;

            // game manager
            var gmGo = new GameObject("GameManager", typeof(GameManager));
            var gm = gmGo.GetComponent<GameManager>();
            gm.cam = cam;
            gm.tubePrefab = tubePrefab;
            gm.ballPrefab = ballPrefab;
            gm.tubeSprite = LoadSprite(Root + "/Art/Tubes/Tube.png");
            gm.smallTubeSprite = LoadSprite(Root + "/Art/Tubes/Tube short.PNG");
            var balls = new List<Sprite>();
            for (int i = 0; i < 13; i++)
            {
                var s = LoadSprite(Root + $"/Art/Balls/ball_{i:00}.png");
                if (s != null) balls.Add(s);
            }
            gm.ballSprites = balls.ToArray();
            gm.heavyBallSprite = LoadSprite(Root + "/Art/Balls/NewBalls/Cannonball.png");
            // imported as a sprite sheet (Multiple): take its first sprite
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Root + "/Art/Tubes/Hidden tube.png"))
                if (o is Sprite hidden) { gm.hiddenTubeSprite = hidden; break; }
            var tubesRoot = new GameObject("Tubes").transform;
            tubesRoot.SetParent(gmGo.transform, false);
            gm.tubesRoot = tubesRoot;
            gm.levelPackResource = "data/levels";

            // event system
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // HUD
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GameHUD));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            FillHud(canvasGo.GetComponent<GameHUD>(), gm, skin);
            MainMenuBuilder.Add();
            ApplyWordArt(canvasGo.GetComponent<GameHUD>(), skin);
            BackgroundStarsBuilder.Add();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes) if (s.path == path) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Mockup background (dark navy) behind the board; the camera clears to the same tone.</summary>
        static void ApplyBackground(UISkin skin)
        {
            var bg = GameObject.Find("Background");
            if (bg != null && skin.background != null && bg.TryGetComponent(out SpriteRenderer sr))
            {
                sr.sprite = skin.background;
                EditorUtility.SetDirty(sr);
            }
            var cam = Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = CamBackground;
                EditorUtility.SetDirty(cam);
            }
        }

        // ------------------------------------------------------------------ lettering
        /// <summary>The skin's font (a TTF/OTF in Art/Fonts), else Unity's built-in one.</summary>
        internal static Font UIFont
        {
            get
            {
                var skin = AssetDatabase.LoadAssetAtPath<UISkin>(NewUISkinBuilder.SkinPath);
                return skin != null && skin.font != null ? skin.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        /// <summary>
        /// Swaps the HUD's lettering for the Art/Fonts pictures without moving anything: the LEVEL pills become
        /// picture rows (<see cref="WordLabel"/>), the popup titles and the CONTINUE / home / try again buttons show
        /// their word pictures, and every Text takes the skin's font when there is one. Safe to run again.
        /// Menu: BallSort > Apply Word Art (also run by Rebuild HUD and Build Example Scene).
        /// </summary>
        [MenuItem("BallSort/Apply Word Art")]
        public static void ApplyWordArt()
        {
            var hud = Object.FindFirstObjectByType<GameHUD>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("Apply Word Art: no GameHUD in the open scene.");
                return;
            }
            var skin = NewUISkinBuilder.BuildSkin();
            Undo.RegisterFullObjectHierarchyUndo(hud.gameObject, "Apply Word Art");
            ApplyWordArt(hud, skin);
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("Apply Word Art: lettering pictures applied and the scene saved.");
        }

        internal static void ApplyWordArt(GameHUD hud, UISkin skin)
        {
            if (skin.font != null)
                foreach (var t in hud.GetComponentsInChildren<Text>(true)) { t.font = skin.font; t.fontStyle = WordArt.Style(t.fontStyle); EditorUtility.SetDirty(t); }
            if (hud.levelText != null) LevelLabel(hud.levelText, skin.wordLevel);
            if (hud.mainMenu != null && hud.mainMenu.levelText != null) LevelLabel(hud.mainMenu.levelText, skin.wordLevel);
            WordArt.Picture(hud.successTitle, skin.wordWellDone);
            WordArt.Picture(ButtonLabel(hud.nextButton), skin.wordContinue);
            WordArt.Picture(ButtonLabel(hud.noMoveContinueButton), skin.wordContinue);
            WordArt.Picture(ButtonLabel(hud.noMoveHomeButton), skin.wordHome);
            WordArt.Picture(ButtonLabel(hud.noMoveReplayButton), skin.wordTryAgain);
            var loseTitle = hud.noMoveView != null ? hud.noMoveView.transform.Find("panel/title") : null;
            if (loseTitle != null) WordArt.Picture(loseTitle.GetComponent<Text>(), skin.wordOutOfMoves);
        }

        static Text ButtonLabel(Button b)
        {
            var label = b != null ? b.transform.Find("label") : null;
            return label != null ? label.GetComponent<Text>() : null;
        }

        /// <summary>
        /// Turns a "LEVEL n" Text into a picture row (<see cref="WordLabel"/>): the LEVEL word picture, then the digit
        /// pictures, the Text kept in the row as the fallback. Already converted texts are left alone.
        /// </summary>
        /// <summary>The LEVEL lettering is drawn 1.5x the mockup export (the user asked for bigger level titles); the pills are sized to match.</summary>
        internal const float LevelScale = 1.5f;

        internal static WordLabel LevelLabel(Text text, Sprite word)
        {
            var existing = text.GetComponentInParent<WordLabel>(true);
            if (existing != null)
            {
                if (existing.word != null) { existing.word.sprite = word; existing.word.rectTransform.sizeDelta = WordArt.Size(word) * LevelScale; }
                if (word != null) existing.digitHeight = WordArt.Size(word).y * LevelScale;
                existing.Set(text.text, 1);
                return existing;
            }
            var rt = text.rectTransform;
            var row = MakeRect("label", rt.parent, rt.anchorMin, rt.anchorMax, rt.pivot, rt.anchoredPosition, rt.sizeDelta);
            row.SetSiblingIndex(rt.GetSiblingIndex());
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.childAlignment = TextAnchor.MiddleCenter;
            group.spacing = 6f * WordArt.MockupScale * LevelScale;   // the gap between LEVEL and 1 in the export
            group.childControlWidth = group.childControlHeight = false;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            var label = row.gameObject.AddComponent<WordLabel>();
            label.row = row;
            label.fallback = text;
            var img = MakeImage(WordArt.PictureName, row, Center, Center, Center, Vector2.zero, WordArt.Size(word) * LevelScale, word, Color.white, Image.Type.Simple);
            img.preserveAspect = true;
            img.raycastTarget = false;
            label.word = img;
            if (word != null) label.digitHeight = WordArt.Size(word).y * LevelScale;
            rt.SetParent(row, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = Center;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(200f, 60f) * LevelScale;
            text.fontSize = Mathf.RoundToInt(text.fontSize * LevelScale);
            label.Set(text.text, 1);
            return label;
        }

        // ------------------------------------------------------------------ HUD

        /// <summary>Builds every HUD element under <paramref name="hud"/> and wires its fields.</summary>
        internal static void FillHud(GameHUD hud, GameManager gm, UISkin skin)
        {
            hud.game = gm;
            var root = (RectTransform)hud.transform;
            if (gm != null)
            {
                gm.TopBarScreenFraction = 0.129f;    // LEVEL pill bottom: 301 of 2340 px (GameHUD measures the real value at runtime)
                gm.BottomBarScreenFraction = 0.120f; // booster bar top: 280 of 2340 px
                gm.BoardOffsetFactor = 0f;           // the board sits in the middle of the band between the bars
                EditorUtility.SetDirty(gm);
            }

            // ---- top bar: restart left, gold LEVEL pill under the middle, settings right (the mini game opens from the main menu)
            var topBar = MakeRect("top_bar", root, new Vector2(0, 1), new Vector2(1, 1), Center, new Vector2(0, -140), new Vector2(0, 280));
            hud.resetButton = IconButton("btn - restart", topBar, new Vector2(0, 0.5f), new Vector2(121, 7), skin.buttonSquare, 146, skin.iconRestart, 100);
            hud.miniGameButton = null;
            hud.settingsButton = IconButton("btn - settings", topBar, new Vector2(1, 0.5f), new Vector2(-121, 7), skin.buttonSquare, 146, skin.iconSettings, 100);
            var levelBg = MakeImage("level_pill", topBar, Center, Center, Center, new Vector2(0, -112), new Vector2(400, 150), skin.buttonPill, Color.white, Image.Type.Sliced);
            hud.levelText = MakeText("text", levelBg.rectTransform, Vector2.zero, Vector2.one, Center, new Vector2(0, 2), new Vector2(-24, 0), "LEVEL 1", 54, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            AddShadow(hud.levelText.gameObject);

            // ---- booster bar: gold-framed panel running off the bottom edge, undo / shuffle / add tube / little tube and the 'i' button
            var bar = MakeImage("booster_bar", root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Center, new Vector2(0, 110), new Vector2(776, 340), skin.pill, Color.white, Image.Type.Sliced);
            hud.undoButton = Booster("btn - undo", bar.transform, -270, 20, skin, skin.iconUndo, out var undoCount, "5");
            hud.shuffleButton = Booster("btn - shuffle", bar.transform, -90, 20, skin, skin.iconShuffle, out _, null);
            hud.addTubeButton = Booster("btn - add_tube", bar.transform, 90, 20, skin, skin.iconAddTube, out var addCount, "1");
            hud.smallTubeButton = Booster("btn - add_small_tube", bar.transform, 270, 20, skin, skin.iconAddSmallTube, out var smallCount, "1");
            hud.undoCountText = undoCount;
            hud.addTubeCountText = addCount;
            hud.smallTubeCountText = smallCount;
            var info = MakeImage("btn - info", bar.transform, new Vector2(0, 1), new Vector2(0, 1), Center, new Vector2(26, -4), new Vector2(64, 64), skin.buttonCircle, Color.white, Image.Type.Simple);
            hud.boosterInfoButton = info.gameObject.AddComponent<Button>();
            hud.boosterInfoButton.targetGraphic = info;
            MakeText("i", info.transform, Vector2.zero, Vector2.one, Center, new Vector2(0, 2), Vector2.zero, "i", 44, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter, Color.white);

            // ---- booster info panel: flat pink panel above the bar, left; tapping it closes it
            var infoPanel = MakeImage("booster_info", root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Center, new Vector2(-176, 575), new Vector2(461, 566), skin.panelFlat, Color.white, Image.Type.Sliced);
            infoPanel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var infoTitle = MakeText("title", infoPanel.transform, Center, Center, Center, new Vector2(0, 218), new Vector2(400, 60), "BOOSTERS", 44, FontStyle.Bold, TextAnchor.MiddleCenter, Navy);
            AddShadow(infoTitle.gameObject, new Color(1f, 1f, 1f, 0.25f));
            InfoRow(infoPanel.transform, 118, skin.iconUndo, "Undo your move");
            InfoRow(infoPanel.transform, 21, skin.iconShuffle, "Shuffle your balls");
            InfoRow(infoPanel.transform, -92, skin.iconAddTube, "Add a tube");
            InfoRow(infoPanel.transform, -205, skin.iconAddSmallTube, "Add a little tube");
            hud.boosterInfoPanel = infoPanel.gameObject;

            // ---- toast: above the booster bar
            var toast = MakeImage("toast", root, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Center, new Vector2(0, 470), new Vector2(840, 96), skin.buttonPill, Color.white, Image.Type.Sliced);
            hud.toast = toast.gameObject;
            hud.toastText = MakeText("text", toast.rectTransform, Vector2.zero, Vector2.one, Center, Vector2.zero, new Vector2(-40, 0), "", 44, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);

            // ---- settings page: dim, gold-framed panel, round back button, three rows (button + striped level bar), MAIN MENU under the panel
            var settings = MakeRect("settings_view", root, Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero);
            MakeImage("dim", settings, Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero, null, Dim, Image.Type.Simple);
            var settingsPanel = MakeImage("panel", settings, Center, Center, Center, Vector2.zero, new Vector2(873, 1412), skin.settingsBackground, Color.white, Image.Type.Sliced);
            var back = MakeImage("btn - back", settingsPanel.transform, Center, Center, Center, new Vector2(-322, 614), new Vector2(130, 130), skin.buttonCircle, Color.white, Image.Type.Simple);
            var backIcon = Decor("img - icon", back.transform, skin.iconUndo, new Vector2(0, 4), 82);
            backIcon.rectTransform.localScale = new Vector3(-1f, 1f, 1f);   // the undo arrow, mirrored, is the mockup's back arrow
            hud.settingsCloseButton = back.gameObject.AddComponent<Button>();
            hud.settingsCloseButton.targetGraphic = back;
            var view = settings.gameObject.AddComponent<SettingsView>();
            view.haptic = SettingsRow(settingsPanel.transform, 437, skin, skin.iconHaptic);
            view.music = SettingsRow(settingsPanel.transform, 0, skin, skin.iconMusic);
            view.sound = SettingsRow(settingsPanel.transform, -437, skin, skin.iconSound);
            hud.homeButton = PillButton("btn - main menu", settings, new Vector2(0, -870), new Vector2(520, 170), skin.buttonPill, "MAIN MENU", 56);
            hud.settingsPanel = settings.gameObject;

            // ---- Well done: gold-framed panel at its own aspect, title, coin in a round button, CONTINUE inside the bottom edge
            var success = MakeRect("SuccessView", root, Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero);
            MakeImage("dim", success, Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero, null, Dim, Image.Type.Simple);
            // the picture has ~50 px of transparent margin around its frame (sparkles): 925 x 1050 shows a 776 x 885 frame
            var winPanel = MakeImage("panel", success, Center, Center, Center, new Vector2(0, 20), new Vector2(925, 1050), skin.popupPanel, Color.white, Image.Type.Simple);
            hud.successTitle = Title("title", winPanel.transform, new Vector2(0, 345), new Vector2(700, 100), "WELL DONE!", 64);
            Decor("ring", winPanel.transform, skin.buttonCircle, new Vector2(0, 80), 380);
            Decor("coin", winPanel.transform, skin.coin, new Vector2(0, 84), 250);
            hud.nextButton = PillButton("btn - continue", winPanel.transform, new Vector2(0, -320), new Vector2(520, 170), skin.buttonPill, "CONTINUE", 56);
            hud.successView = success.gameObject;

            // ---- Out of moves: gold-framed panel with the paid CONTINUE (coins, rewinds moves) in the middle, home / try again inside the bottom edge
            var noMove = MakeRect("NoMoveView", root, Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero);
            MakeImage("dim", noMove, Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero, null, Dim, Image.Type.Simple);
            var losePanel = MakeImage("panel", noMove, Center, Center, Center, new Vector2(0, 40), new Vector2(776, 690), skin.popupPanel, Color.white, Image.Type.Sliced);
            Title("title", losePanel.transform, new Vector2(0, 235), new Vector2(700, 100), "OUT OF MOVES :(", 64);
            hud.noMoveContinueButton = PillButton("btn - continue", losePanel.transform, new Vector2(0, 40), new Vector2(560, 160), skin.buttonRectGreen != null ? skin.buttonRectGreen : skin.buttonPill, "CONTINUE", 50);
            hud.noMoveContinueCostText = CoinPrice(hud.noMoveContinueButton, skin, hud.continueCost);
            var continueColors = hud.noMoveContinueButton.colors; continueColors.disabledColor = new Color(1, 1, 1, 0.45f); hud.noMoveContinueButton.colors = continueColors;
            hud.noMoveHomeButton = PillButton("btn - home", losePanel.transform, new Vector2(-185, -195), new Vector2(340, 140), skin.buttonPill, "home", 52);
            hud.noMoveReplayButton = PillButton("btn - try_again", losePanel.transform, new Vector2(185, -195), new Vector2(340, 140), skin.buttonPill, "try again", 52);
            hud.noMoveView = noMove.gameObject;
            hud.noMoveUndoButton = null;
            hud.noMoveAddTubeButton = null;
            hud.noMoveCloseButton = null;

            infoPanel.gameObject.SetActive(false);
            toast.gameObject.SetActive(false);
            settings.gameObject.SetActive(false);
            success.gameObject.SetActive(false);
            noMove.gameObject.SetActive(false);
            EditorUtility.SetDirty(hud);
        }

        // ------------------------------------------------------------------ UI helpers
        internal static RectTransform MakeRect(string name, Transform parent, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = amin; rt.anchorMax = amax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        internal static Image MakeImage(string name, Transform parent, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 pos, Vector2 size, Sprite sprite, Color color, Image.Type type)
        {
            var rt = MakeRect(name, parent, amin, amax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = type;
            img.raycastTarget = true;
            if (type == Image.Type.Sliced) RoundEnds(img);
            return img;
        }

        /// <summary>
        /// A sliced stadium sprite (borders of half its height: Button elipse, Reward row) drawn taller than itself
        /// would get straight bits between its rounded ends; scaling the slices to the image height keeps the ends
        /// round. Other sliced sprites are left at 1:1. Call again after resizing the image.
        /// </summary>
        public static void RoundEnds(Image img)
        {
            var s = img != null ? img.sprite : null;
            if (s == null || img.type != Image.Type.Sliced) return;
            float ends = s.border.y + s.border.w;
            float height = img.rectTransform.rect.height;
            if (height <= 0f) height = img.rectTransform.sizeDelta.y;
            img.pixelsPerUnitMultiplier = ends >= s.rect.height * 0.9f && height > 0f ? ends / height : 1f;
        }

        internal static Text MakeText(string name, Transform parent, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 pos, Vector2 size, string text, int fontSize, FontStyle style, TextAnchor anchor, Color color)
        {
            var rt = MakeRect(name, parent, amin, amax, pivot, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UIFont;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = WordArt.Style(style);
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        internal static void AddShadow(GameObject go) => AddShadow(go, new Color(0, 0, 0, 0.6f));

        internal static void AddShadow(GameObject go, Color color)
        {
            var s = go.AddComponent<Shadow>();
            s.effectColor = color;
            s.effectDistance = new Vector2(0, -5);
        }

        /// <summary>Gold popup title with the navy outline of the mockups.</summary>
        internal static Text Title(string name, Transform parent, Vector2 pos, Vector2 size, string text, int fontSize)
        {
            var t = MakeText(name, parent, Center, Center, Center, pos, size, text, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = Navy;
            o.effectDistance = new Vector2(4, -4);
            AddShadow(t.gameObject);
            return t;
        }

        /// <summary>Non-interactive picture centred at <paramref name="pos"/>.</summary>
        internal static Image Decor(string name, Transform parent, Sprite sprite, Vector2 pos, float size)
        {
            var img = MakeImage(name, parent, Center, Center, Center, pos, new Vector2(size, size), sprite, Color.white, Image.Type.Simple);
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        internal static Button IconButton(string name, Transform parent, Vector2 anchor, Vector2 pos, Sprite bg, float bgSize, Sprite icon, float iconSize, bool sliced = true)
        {
            var img = MakeImage(name, parent, anchor, anchor, Center, pos, new Vector2(bgSize, bgSize), bg, Color.white, sliced ? Image.Type.Sliced : Image.Type.Simple);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var iconImg = Decor("icon", img.transform, icon, new Vector2(0, 4), iconSize);
            iconImg.name = "img - icon";
            return btn;
        }

        /// <summary>Booster bar button: 146 square, 100 icon, red count badge at the top right corner.</summary>
        static Button Booster(string name, Transform parent, float x, float y, UISkin skin, Sprite icon, out Text countText, string count)
        {
            var btn = IconButton(name, parent, Center, new Vector2(x, y), skin.buttonSquare, 146, icon, 100);
            var colors = btn.colors; colors.disabledColor = new Color(1, 1, 1, 0.45f); btn.colors = colors;
            countText = null;
            if (count != null)
            {
                var badge = MakeImage("count_badge", btn.transform, Center, Center, Center, new Vector2(58, 58), new Vector2(56, 56), skin.redCircle, Color.white, Image.Type.Simple);
                badge.raycastTarget = false;
                countText = MakeText("count", badge.transform, Vector2.zero, Vector2.one, Center, new Vector2(0, 2), Vector2.zero, count, 34, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            }
            return btn;
        }

        static void InfoRow(Transform parent, float y, Sprite icon, string label)
        {
            Decor("icon", parent, icon, new Vector2(-150, y), 76);
            var t = MakeText("label", parent, Center, Center, Center, new Vector2(50, y), new Vector2(300, 60), label, 34, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            AddShadow(t.gameObject);
        }

        /// <summary>Settings row: 307 square button with its icon, striped level bar (a Slider) 178 below it.</summary>
        static SettingsView.Row SettingsRow(Transform parent, float y, UISkin skin, Sprite icon)
        {
            var row = new SettingsView.Row();
            var b = MakeImage("btn", parent, Center, Center, Center, new Vector2(0, y), new Vector2(307, 307), skin.buttonSquare, Color.white, Image.Type.Sliced);
            row.button = b.gameObject.AddComponent<Button>();
            row.button.targetGraphic = b;
            row.icon = Decor("img - icon", b.transform, icon, new Vector2(0, 8), 196);

            var track = MakeImage("bar", parent, Center, Center, Center, new Vector2(0, y - 178), new Vector2(517, 73), skin.settingsBar, new Color(1f, 1f, 1f, 0.3f), Image.Type.Simple);
            var fill = MakeImage("fill", track.transform, Vector2.zero, Vector2.one, Center, Vector2.zero, Vector2.zero, skin.settingsBar, Color.white, Image.Type.Filled);
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.raycastTarget = false;
            var slider = track.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.fillRect = fill.rectTransform;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            row.slider = slider;
            return row;
        }

        /// <summary>
        /// Turns a pill button into a two-line price button: the label moves up, and "900 x [coin]" sits under it.
        /// Returns the price text, so the HUD can keep it in sync with its cost field.
        /// </summary>
        static Text CoinPrice(Button btn, UISkin skin, int price)
        {
            var label = btn.transform.Find("label") as RectTransform;
            if (label != null) { label.anchoredPosition = new Vector2(0, 34); }
            var row = MakeRect("price", btn.transform, Center, Center, Center, new Vector2(0, -32), new Vector2(300, 60));
            var t = MakeText("cost", row, Center, Center, new Vector2(1f, 0.5f), new Vector2(18, 2), new Vector2(200, 60), price + " x", 44, FontStyle.Bold, TextAnchor.MiddleRight, Color.white);
            AddShadow(t.gameObject);
            Decor("coin", row, skin.coin, new Vector2(58, 0), 64).raycastTarget = false;
            return t;
        }

        internal static Button PillButton(string name, Transform parent, Vector2 pos, Vector2 size, Sprite bg, string label, int fontSize)
        {
            var img = MakeImage(name, parent, Center, Center, Center, pos, size, bg, Color.white, Image.Type.Sliced);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var t = MakeText("label", img.transform, Vector2.zero, Vector2.one, Center, new Vector2(0, 4), new Vector2(-30, 0), label, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            AddShadow(t.gameObject);
            return btn;
        }
    }
}
