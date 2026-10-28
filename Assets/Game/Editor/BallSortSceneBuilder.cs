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
    /// One-shot builder: configures the imported art, creates the Tube/Ball prefabs, and builds the example scene
    /// with the HUD laid out like the reference game's GameViewV3 (1080x2340 reference resolution).
    /// Menu: BallSort > Build Example Scene.
    /// </summary>
    public static class BallSortSceneBuilder
    {
        const string Root = "Assets/Game";
        const string ArtUI = Root + "/Art/UI/";
        const string ScenePath = Root + "/Scenes/BallSortExample.unity";

        static readonly Color Purple = new Color(0.49f, 0.45f, 0.93f, 1f);     // tint for the grey round-rect button bg
        static readonly Color TopBarIcon = Color.white;

        [MenuItem("BallSort/Build Example Scene")]
        public static void Build()
        {
            ConfigureArt();
            var tubePrefab = BuildTubePrefab();
            var ballPrefab = BuildBallPrefab();
            BuildScene(tubePrefab, ballPrefab);
            Debug.Log("BallSort example scene built: " + ScenePath);
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
            sr.sprite = LoadSprite(Root + "/Art/Tubes/default_tube_v3.png");
            sr.sortingOrder = 3;
            var col = go.GetComponent<BoxCollider2D>();
            col.size = new Vector2(1.58f, 6.4f);
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
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // camera
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.114f, 0.114f, 0.114f, 1f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);

            // 2D global light so lit sprite materials render under the URP 2D renderer
            var lightGo = new GameObject("Global Light 2D", typeof(Light2D));
            var light = lightGo.GetComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;

            // background
            var bgGo = new GameObject("Background", typeof(SpriteRenderer), typeof(BackgroundFitter));
            var bgSr = bgGo.GetComponent<SpriteRenderer>();
            bgSr.sprite = LoadSprite(Root + "/Art/Backgrounds/theme_default_v3.png");
            bgSr.sortingOrder = -10;
            bgGo.GetComponent<BackgroundFitter>().cam = cam;

            // game manager
            var gmGo = new GameObject("GameMgr", typeof(GameMgr));
            var gm = gmGo.GetComponent<GameMgr>();
            gm.cam = cam;
            gm.tubePrefab = tubePrefab;
            gm.ballPrefab = ballPrefab;
            gm.tubeSprite = LoadSprite(Root + "/Art/Tubes/default_tube_v3.png");
            var balls = new List<Sprite>();
            for (int i = 0; i < 13; i++)
            {
                var s = LoadSprite(Root + $"/Art/Balls/ball_{i:00}.png");
                if (s != null) balls.Add(s);
            }
            gm.ballSprites = balls.ToArray();
            var tubesRoot = new GameObject("Tubes").transform;
            tubesRoot.SetParent(gmGo.transform, false);
            gm.tubesRoot = tubesRoot;
            gm.levelPackResource = "data/lv_1_100";
            gm.startLevel = 1;

            // event system
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // HUD
            BuildHud(gm);

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

        // ------------------------------------------------------------------ HUD
        static Font _font;
        static Font UIFont => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        static void BuildHud(GameMgr gm)
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GameHUD));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            var hud = canvasGo.GetComponent<GameHUD>();
            hud.game = gm;
            var root = canvasGo.GetComponent<RectTransform>();

            // ---- top bar: height 172 anchored to the top (GameViewV3/content/game_top_bar)
            var topBar = MakeRect("game_top_bar", root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -86), new Vector2(0, 172));

            // reset button (left, x=97): dark circle 114 + exchange icon 88
            var resetBtn = IconButton("btn - reset", topBar, new Vector2(0, 0.5f), new Vector2(97, 0), "ui_topbar_circle_114", Color.white, 114, "ic_restart", 88);
            hud.resetButton = resetBtn;

            // settings button (right, x=-97)
            var settingsBtn = IconButton("btn - setting", topBar, new Vector2(1, 0.5f), new Vector2(-97, 0), "ui_topbar_circle_114", Color.white, 114, "ic_settings", 88);
            hud.settingsButton = settingsBtn;

            // level pill (middle): difficulity_normal 373x132 with the level text
            var levelBg = MakeImage("difficulity_normal", topBar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(373, 132), "ui_level_pill", Color.white, Image.Type.Sliced);
            var levelText = MakeText("text", levelBg.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24, 73), "Level 1", 62, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            hud.levelText = levelText;

            // ---- settings panel: vertical strip under the settings button (GameViewV3/content/game_settings/buttons)
            var settingsPanel = MakeRect("game_settings", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var dim = MakeImage("bg", settingsPanel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, null, new Color(0, 0, 0, 0.55f), Image.Type.Simple);
            var dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            var buttons = MakeRect("buttons", settingsPanel, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-97, -323), new Vector2(114, 588));
            var settingClose = IconButton("btn - setting", buttons, new Vector2(0.5f, 1), new Vector2(0, -57), "ui_topbar_circle_114", Purple, 114, "ic_close", 60);
            var homeBtn = IconButton("home_button", buttons, new Vector2(0.5f, 1), new Vector2(0, -215), "ui_circle_red_114", Color.white, 114, "ic_home", 88);
            hud.settingsPanel = settingsPanel.gameObject;
            hud.settingsCloseButton = settingClose;
            hud.homeButton = homeBtn;
            dimBtn.onClick.AddListener(() => settingsPanel.gameObject.SetActive(false));

            // ---- bottom bar: y=240 from the bottom, height 168 (GameViewV3/Bottom)
            var bottom = MakeRect("Bottom", root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 240), new Vector2(0, 168));
            hud.undoButton = BoosterButton("btn - undo", bottom, -268, "ic_undo", 120, out var undoCount, "5");
            hud.undoCountText = undoCount;
            hud.shuffleButton = BoosterButton("btn - shuffle", bottom, 0, "ic_shuffle", 120, out _, null);
            hud.addTubeButton = BoosterButton("btn - add_tube", bottom, 268, "ic_add_tube", 120, out var addCount, "1");
            hud.addTubeCountText = addCount;

            // ---- toast
            var toast = MakeImage("toast", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -420), new Vector2(840, 96), "ui_dark_pill", Color.white, Image.Type.Sliced);
            hud.toast = toast.gameObject;
            hud.toastText = MakeText("text", toast.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-40, 0), "", 44, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);

            // ---- SuccessView
            var success = MakeRect("SuccessViewV3", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            MakeImage("dim", success, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, null, new Color(0.04f, 0.03f, 0.12f, 0.82f), Image.Type.Simple);
            var wellDone = MakeText("Well Done!", success, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 420), new Vector2(938, 135), "Well Done!", 110, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.87f, 0.3f));
            AddShadow(wellDone.gameObject);
            var nextBtn = TextButton("btn_next", success, new Vector2(0.5f, 0.5f), new Vector2(0, -520), new Vector2(680, 216), "ui_button_green", "Next", 92);
            hud.successView = success.gameObject;
            hud.successTitle = wellDone;
            hud.nextButton = nextBtn;

            // ---- NoMoveView
            var noMove = MakeRect("NoMoveView", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            MakeImage("bg", noMove, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, null, new Color(0.04f, 0.03f, 0.12f, 0.86f), Image.Type.Simple);
            var title = MakeText("Title", noMove, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -620), new Vector2(900, 120), "No more moves!", 92, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            AddShadow(title.gameObject);
            var closeNoMove = IconButton("CloseNoMovePanelButton", noMove, new Vector2(1, 1), new Vector2(-100, -160), "ui_topbar_circle_114", Purple, 114, "ic_close", 60);
            var replay = TextButton("ReplayButton", noMove, new Vector2(0.5f, 0), new Vector2(-250, 640), new Vector2(452, 174), "ui_button_green", "Replay", 70);
            var playOn = TextButton("PlayOnButton", noMove, new Vector2(0.5f, 0), new Vector2(250, 640), new Vector2(452, 174), "ui_button_green", "Add Tube", 70);
            var undoBtn2 = TextButton("UndoButton", noMove, new Vector2(0.5f, 0), new Vector2(0, 430), new Vector2(452, 150), "ui_dark_pill", "Undo", 64);
            hud.noMoveView = noMove.gameObject;
            hud.noMoveReplayButton = replay;
            hud.noMoveAddTubeButton = playOn;
            hud.noMoveUndoButton = undoBtn2;
            hud.noMoveCloseButton = closeNoMove;

            settingsPanel.gameObject.SetActive(false);
            toast.gameObject.SetActive(false);
            success.gameObject.SetActive(false);
            noMove.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ UI helpers
        static RectTransform MakeRect(string name, Transform parent, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = amin; rt.anchorMax = amax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        static Image MakeImage(string name, Transform parent, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 pos, Vector2 size, string spriteName, Color color, Image.Type type)
        {
            var rt = MakeRect(name, parent, amin, amax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            if (!string.IsNullOrEmpty(spriteName)) img.sprite = LoadSprite(ArtUI + spriteName + ".png");
            img.color = color;
            img.type = type;
            img.raycastTarget = true;
            return img;
        }

        static Text MakeText(string name, Transform parent, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 pos, Vector2 size, string text, int fontSize, FontStyle style, TextAnchor anchor, Color color)
        {
            var rt = MakeRect(name, parent, amin, amax, pivot, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UIFont;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static void AddShadow(GameObject go)
        {
            var s = go.AddComponent<Shadow>();
            s.effectColor = new Color(0, 0, 0, 0.6f);
            s.effectDistance = new Vector2(0, -6);
        }

        static Button IconButton(string name, Transform parent, Vector2 anchor, Vector2 pos, string bgSprite, Color bgColor, float bgSize, string iconSprite, float iconSize)
        {
            var bg = MakeImage(name, parent, anchor, anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(bgSize, bgSize), bgSprite, bgColor, Image.Type.Simple);
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var icon = MakeImage("img - " + iconSprite, bg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconSize, iconSize), iconSprite, TopBarIcon, Image.Type.Simple);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            return btn;
        }

        /// <summary>Bottom-bar booster: 168x168 purple round rect, 120 icon, red count badge at (74,-72) (reference btn - undo).</summary>
        static Button BoosterButton(string name, Transform parent, float x, string iconSprite, float iconSize, out Text countText, string count)
        {
            var bg = MakeImage(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(168, 168), "ui_round_rect_168", Purple, Image.Type.Sliced);
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors; colors.disabledColor = new Color(1, 1, 1, 0.45f); btn.colors = colors;
            var icon = MakeImage("img - " + iconSprite, bg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(iconSize, iconSize), iconSprite, Color.white, Image.Type.Simple);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            countText = null;
            if (count != null)
            {
                var badge = MakeImage("count_badge", bg.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(74, 72), new Vector2(64, 64), "ui_badge_red", Color.white, Image.Type.Simple);
                badge.raycastTarget = false;
                countText = MakeText("count", badge.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0, 2), Vector2.zero, count, 40, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            }
            return btn;
        }

        static Button TextButton(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, string bgSprite, string label, int fontSize)
        {
            var bg = MakeImage(name, parent, anchor, anchor, new Vector2(0.5f, 0.5f), pos, size, bgSprite, Color.white, Image.Type.Sliced);
            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var t = MakeText("label", bg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0, 4), new Vector2(-40, 0), label, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            AddShadow(t.gameObject);
            return btn;
        }
    }
}
