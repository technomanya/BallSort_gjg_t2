using System.Collections.Generic;
using System.IO;
using BallSortGame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// Applies the Art/UI/NewUI kit: sets 9-slice borders on the stretchable sprites, fills Resources/UISkin.asset (used by the scene builder and by UI built at runtime, e.g. Jump Path)
    /// and reskins the GameHUD of the open scene in place, so a hand-edited scene keeps its layout.
    /// Menu: BallSort > Apply New UI. (BallSort > Rebuild HUD lays the HUD out afresh from the same skin.)
    /// </summary>
    public static class NewUISkinBuilder
    {
        const string Dir = "Assets/Game/Art/UI/NewUI/";
        const string BackgroundDir = "Assets/Game/Art/Backgrounds/";
        const string GeneratedDir = "Assets/Game/Art/UI/Generated/";
        internal const string SkinPath = "Assets/Game/Resources/UISkin.asset";
        const string FontsDir = "Assets/Game/Art/Fonts/";

        /// <summary>
        /// Per file: visible part of the texture (x, y from the bottom, width, height; null = whole) and border
        /// (left, bottom, right, top). The gold-framed panels carry a sparkle on the middle of each side, so they are
        /// used at (or near) their own height and stretched sideways only; the stadium shapes (Button elipse, Reward
        /// row) have borders of half their height, and the scene builders scale them so the ends stay round.
        /// </summary>
        static readonly Dictionary<string, (RectInt? crop, Vector4 border)> Slices = new Dictionary<string, (RectInt?, Vector4)>
        {
            { "Button rectangle.PNG", (null, new Vector4(40, 40, 40, 40)) },
            { "Button rectangle hard level.PNG", (null, new Vector4(40, 40, 40, 40)) },
            { "Button rectangle super hard level.PNG", (null, new Vector4(40, 40, 40, 40)) },
            { "Button square.PNG", (null, new Vector4(45, 45, 45, 45)) },
            { "Square frame.PNG", (null, new Vector4(50, 50, 50, 50)) },
            { "Button elipse.PNG", (null, new Vector4(40, 38, 40, 38)) },
            { "Big pop up shaded.PNG", (null, new Vector4(70, 70, 70, 70)) },
            { "Small pop up shaded.PNG", (null, new Vector4(60, 60, 60, 60)) },
            { "small pop up FLAT shade.png", (null, new Vector4(40, 40, 40, 40)) },
            { "Settings background.png", (null, new Vector4(24, 24, 24, 24)) },
            { "FLAT pink background for settings.png", (null, new Vector4(24, 24, 24, 24)) },
            { "Reward row.png", (null, new Vector4(29, 28, 29, 28)) },
            { "Reward tile back.png", (null, new Vector4(20, 20, 20, 20)) },
        };

        [MenuItem("BallSort/Apply New UI")]
        public static void Apply()
        {
            var skin = BuildSkin();
            ReskinScene(skin);
        }

        // ------------------------------------------------------------------ sprites + skin asset

        public static UISkin BuildSkin()
        {
            ConfigureSlices();
            var skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<UISkin>();
                AssetDatabase.CreateAsset(skin, SkinPath);
            }
            skin.buttonCircle = S("Button circle.PNG");
            skin.buttonSquare = S("Button square.PNG");
            skin.buttonRect = S("Button rectangle.PNG");
            skin.buttonRectGreen = S("Button rectangle hard level.PNG");
            skin.buttonRectRed = S("Button rectangle super hard level.PNG");
            skin.circleFrame = S("Circle frame.PNG");
            skin.squareFrame = S("Square frame.PNG");
            skin.greenCircle = S("green dot.png");
            skin.redCircle = S("red dot.png");
            skin.buttonPill = S("Button elipse.PNG");
            skin.popupPanel = S("Big pop up shaded.PNG");
            skin.pill = S("Small pop up shaded.PNG");
            skin.panelFlat = S("small pop up FLAT shade.png");
            skin.overlay = S("Background overlay (pop up ciktiginda ekranin kararmasi icin).png", BackgroundDir);
            skin.settingsBackground = S("Settings background.png");
            skin.settingsBackgroundFlat = S("FLAT pink background for settings.png");
            skin.settingsBar = S("sound_music setting.png");
            skin.rewardStar = S("reward star.PNG");
            skin.rewardPotion = S("reward potion.PNG");
            skin.rewardCrystal = S("reward crystal.PNG");
            skin.rewardSeed = S("Seed plant reward.png");
            skin.rewardRow = S("Reward row.png");
            skin.rewardGlow = S("Reward glow.png");
            skin.cardBackFound = S("Reward tile back.png");
            skin.iconPlant = S("plant.png");
            skin.coin = S("Coin.PNG");
            skin.heart = S("Heart.PNG");
            skin.brokenHeart = S("Out of moves_broken heart.png");
            skin.logo = S("Logo alone.PNG");
            skin.logoFull = S("Logo full.PNG");
            skin.background = S("Background.png", BackgroundDir);
            skin.sparkle = S("Background sparkle.png", BackgroundDir);
            skin.iconClose = S("exit.png");
            skin.iconHome = S("home.png");
            skin.iconSettings = S("settings.png");
            skin.iconRestart = S("restrat.png");
            skin.iconUndo = S("undo (1).png");
            skin.iconShuffle = S("shuffle.png");
            skin.iconAddTube = S("add tube.png");
            skin.iconAddSmallTube = ShortTubeIcon("add_small_tube.png");
            skin.iconSound = S("sound.png");
            skin.iconMusic = S("music.png");
            skin.iconHaptic = S("haptic.png");
            skin.iconCheck = S("check mark.png");
            skin.iconArrow = S("Arrow up and down.png");
            ConfigureWordArt();
            skin.font = FindFont();
            skin.wordLevel = Word("LEVEL 1.png", "LEVEL");
            skin.wordContinue = Word("CONTINUE.png");
            skin.wordHome = Word("Home (1).png");
            skin.wordTryAgain = Word("Try Again.png");
            skin.wordWellDone = Word("WELL DONE!.png");
            skin.wordOutOfMoves = Word("OUT OF MOVES !.png");
            skin.wordManaFlip = Word("Mana Flip.png");
            skin.wordManaGarden = Word("Mana Garden.png");
            skin.digits = Digits();
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssets();
            return skin;
        }

        // ------------------------------------------------------------------ lettering (Art/Fonts)

        /// <summary>The LEVEL 1 export holds two words: the LEVEL word (columns 0-79) and a "1" digit (columns 85-96).</summary>
        static readonly Dictionary<string, (string name, RectInt rect)[]> WordRects = new Dictionary<string, (string, RectInt)[]>
        {
            { "LEVEL 1.png", new[] { ("LEVEL", new RectInt(0, 0, 80, 23)), ("1", new RectInt(85, 0, 12, 23)) } },
        };

        /// <summary>
        /// Every picture under Art/Fonts becomes one sprite covering the whole picture (the folder was auto-sliced on
        /// import, which cut the words into letters), uncompressed so the lettering stays crisp; <see cref="WordRects"/>
        /// names the pictures that hold more than one word.
        /// </summary>
        static void ConfigureWordArt()
        {
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { FontsDir.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || !PngSize(path, out int w, out int h)) continue;
                string file = Path.GetFileName(path);
                if (!WordRects.TryGetValue(file, out var wanted))
                    wanted = new[] { (Path.GetFileNameWithoutExtension(file), new RectInt(0, 0, w, h)) };
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritePixelsPerUnit = 100;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var dp = factory.GetSpriteEditorDataProviderFromObject(importer);
                dp.InitSpriteEditorDataProvider();
                var old = dp.GetSpriteRects();
                var rects = new SpriteRect[wanted.Length];
                for (int i = 0; i < wanted.Length; i++)
                {
                    rects[i] = i < old.Length ? old[i] : new SpriteRect { spriteID = GUID.Generate() };   // keep ids: references survive
                    rects[i].name = wanted[i].name;
                    rects[i].rect = new Rect(wanted[i].rect.x, wanted[i].rect.y, wanted[i].rect.width, wanted[i].rect.height);
                    rects[i].border = Vector4.zero;
                    rects[i].alignment = SpriteAlignment.Center;
                    rects[i].pivot = new Vector2(0.5f, 0.5f);
                }
                dp.SetSpriteRects(rects);
                dp.Apply();
                importer.SaveAndReimport();
            }
        }

        /// <summary>Sprite <paramref name="name"/> (the first one when null) of a picture in Art/Fonts.</summary>
        static Sprite Word(string file, string name = null)
        {
            Sprite first = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FontsDir + file))
            {
                if (!(o is Sprite s)) continue;
                if (name == null || s.name == name) return s;
                if (first == null) first = s;
            }
            if (name == null && first != null) return first;
            Debug.LogWarning("Word picture missing: " + FontsDir + file + (name != null ? " / " + name : ""));
            return null;
        }

        /// <summary>Digit pictures: files named "0.png" .. "9.png" (a copy suffix such as "3 (1).png" is fine); the "1" cut from LEVEL 1.png fills in.</summary>
        static Sprite[] Digits()
        {
            var digits = new Sprite[10];
            var pattern = new System.Text.RegularExpressions.Regex(@"^(\d)( \(\d+\))?\.png$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { FontsDir.TrimEnd('/') }))
            {
                string file = Path.GetFileName(AssetDatabase.GUIDToAssetPath(guid));
                var m = pattern.Match(file);
                if (m.Success) digits[m.Groups[1].Value[0] - '0'] = Word(file);
            }
            if (digits[1] == null) digits[1] = Word("LEVEL 1.png", "1");
            return digits;
        }

        /// <summary>The first TTF/OTF under Art/Fonts, or null (the folder ships pictures only).</summary>
        static Font FindFont()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { FontsDir.TrimEnd('/') }))
            {
                var f = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                if (f != null) return f;
            }
            return null;
        }

        static Sprite S(string file, string dir = Dir)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(dir + file);
            if (s == null) Debug.LogWarning("NewUI sprite missing: " + dir + file);
            return s;
        }

        /// <summary>
        /// Little-tube booster icon, made from the kit's add tube.png so it shares its style: the rim, the band with
        /// the plus and the rounded bottom are kept, the plain glass between them is cut out, and the shorter cup is
        /// scaled up a little and centred. Written to the Generated folder and imported as a sprite.
        /// </summary>
        public static Sprite ShortTubeIcon(string file)
        {
            string path = GeneratedDir + file;
            Directory.CreateDirectory(GeneratedDir);
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(File.ReadAllBytes(Dir + "add tube.png"));
            int w = src.width, h = src.height;
            // bottom-up row ranges of the 120 px icon that are kept: rounded bottom, plus band, rim
            var keep = new List<int>();
            foreach (var r in new[] { new Vector2Int(0, h * 20 / 120), new Vector2Int(h * 41 / 120, h * 78 / 120), new Vector2Int(h * 98 / 120, h - 1) })
                for (int y = r.x; y <= r.y; y++) keep.Add(y);
            var cut = new Texture2D(w, keep.Count, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            int minY = keep.Count, maxY = -1;
            for (int i = 0; i < keep.Count; i++)
                for (int x = 0; x < w; x++)
                {
                    var c = src.GetPixel(x, keep[i]);
                    cut.SetPixel(x, i, c);
                    if (c.a > 0.02f) { minY = Mathf.Min(minY, i); maxY = Mathf.Max(maxY, i); }
                }
            cut.Apply();
            const float scale = 1.15f;
            float cy = (minY + maxY + 1) / 2f;
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sx = (x + 0.5f - w / 2f) / scale + w / 2f;
                    float sy = (y + 0.5f - h / 2f) / scale + cy;
                    bool inside = sx >= 0f && sx <= w && sy >= 0f && sy <= keep.Count;
                    dst.SetPixel(x, y, inside ? cut.GetPixelBilinear(sx / w, sy / keep.Count) : Color.clear);
                }
            dst.Apply();
            File.WriteAllBytes(path, dst.EncodeToPNG());
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(cut);
            Object.DestroyImmediate(dst);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && (imp.textureType != TextureImporterType.Sprite || imp.mipmapEnabled || !imp.alphaIsTransparency))
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Bilinear;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Every kit texture becomes exactly one sprite covering the whole picture (the kit was auto-sliced on import,
        /// which cut icons such as haptic.png and the striped bar into pieces), cropped and bordered per <see cref="Slices"/>.
        /// The first sprite and its id are kept, so existing references survive.
        /// </summary>
        static void ConfigureSlices()
        {
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var files = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir.TrimEnd('/') })) files.Add(AssetDatabase.GUIDToAssetPath(guid));
            files.Add(BackgroundDir + "Background overlay (pop up ciktiginda ekranin kararmasi icin).png");
            foreach (var path in files)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (!PngSize(path, out int w, out int h)) continue;
                Slices.TryGetValue(Path.GetFileName(path), out var slice);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                var dp = factory.GetSpriteEditorDataProviderFromObject(importer);
                dp.InitSpriteEditorDataProvider();
                var rects = dp.GetSpriteRects();
                var r = rects.Length > 0 ? rects[0] : new SpriteRect { name = Path.GetFileNameWithoutExtension(path) + "_0", spriteID = GUID.Generate() };
                r.rect = slice.crop is RectInt c ? new Rect(c.x, c.y, c.width, c.height) : new Rect(0, 0, w, h);
                r.border = slice.border;
                r.alignment = SpriteAlignment.Center;
                r.pivot = new Vector2(0.5f, 0.5f);
                dp.SetSpriteRects(new[] { r });
                dp.Apply();
                importer.SaveAndReimport();
            }
        }

        /// <summary>Width and height from the PNG header (sprite rects are in source pixels).</summary>
        static bool PngSize(string path, out int w, out int h)
        {
            w = h = 0;
            try
            {
                using var f = File.OpenRead(path);
                var b = new byte[24];
                if (f.Read(b, 0, 24) < 24 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return false;
                w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                return w > 0 && h > 0;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ HUD reskin

        /// <summary>Re-applies the skin sprites to the HUD's widgets without moving anything.</summary>
        public static void ReskinScene(UISkin skin)
        {
            var hud = Object.FindFirstObjectByType<GameHUD>(FindObjectsInactive.Include);
            if (hud == null)
            {
                Debug.LogWarning("Apply New UI: no GameHUD in the open scene; only the skin asset was updated.");
                return;
            }
            Undo.RegisterFullObjectHierarchyUndo(hud.gameObject, "Apply New UI");

            // top bar
            IconButton(hud.settingsButton, skin.buttonSquare, skin.iconSettings, true);
            IconButton(hud.miniGameButton, skin.buttonSquare, skin.coin, true);
            if (hud.levelText != null) Bg(hud.levelText.GetComponentInParent<Image>(), skin.buttonPill, true);

            // boosters
            IconButton(hud.undoButton, skin.buttonSquare, skin.iconUndo, true);
            IconButton(hud.shuffleButton, skin.buttonSquare, skin.iconShuffle, true);
            IconButton(hud.addTubeButton, skin.buttonSquare, skin.iconAddTube, true);
            IconButton(hud.smallTubeButton, skin.buttonSquare, skin.iconAddSmallTube, true);
            IconButton(hud.resetButton, skin.buttonSquare, skin.iconRestart, true);
            Badge(hud.undoCountText, skin.redCircle);
            Badge(hud.addTubeCountText, skin.redCircle);
            if (hud.boosterInfoButton != null) Bg(hud.boosterInfoButton.GetComponent<Image>(), skin.buttonCircle, false);
            if (hud.boosterInfoPanel != null) Bg(hud.boosterInfoPanel.GetComponent<Image>(), skin.panelFlat, true);
            var bar = hud.transform.Find("booster_bar");
            if (bar != null) Bg(bar.GetComponent<Image>(), skin.pill, true);

            if (hud.toast != null) Bg(hud.toast.GetComponent<Image>(), skin.buttonPill, true);

            // settings page and popups
            TextButton(hud.homeButton, skin.buttonPill);
            if (hud.settingsCloseButton != null) IconButton(hud.settingsCloseButton, skin.buttonCircle, skin.iconUndo);
            TextButton(hud.nextButton, skin.buttonPill);
            TextButton(hud.noMoveReplayButton, skin.buttonPill);
            TextButton(hud.noMoveHomeButton, skin.buttonPill);
            TextButton(hud.noMoveAddTubeButton, skin.buttonRect);
            TextButton(hud.noMoveUndoButton, skin.buttonRect);
            IconButton(hud.noMoveCloseButton, skin.buttonCircle, skin.iconClose);
            foreach (var view in new[] { hud.successView, hud.noMoveView })
            {
                var panel = view != null ? view.transform.Find("panel") : null;
                if (panel != null) Bg(panel.GetComponent<Image>(), skin.popupPanel, view == hud.noMoveView);
            }

            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
            Debug.Log("Apply New UI: HUD reskinned; save the scene to keep it.");
        }

        static void Bg(Image img, Sprite sprite, bool sliced)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.color = Color.white;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            if (sliced) BallSortSceneBuilder.RoundEnds(img);
            EditorUtility.SetDirty(img);
        }

        /// <summary>Button background plus its icon child ("img - ..." from the scene builder, else the first leaf Image).</summary>
        static void IconButton(Button b, Sprite bg, Sprite icon, bool sliced = false)
        {
            if (b == null) return;
            Bg(b.GetComponent<Image>(), bg, sliced);
            Image target = null;
            foreach (Transform c in b.transform)
            {
                if (!c.TryGetComponent(out Image i) || c.name == "count_badge") continue;
                if (c.name.StartsWith("img")) { target = i; break; }
                if (target == null && c.childCount == 0) target = i;
            }
            if (target == null || icon == null) return;
            target.sprite = icon;
            target.color = Color.white;
            target.preserveAspect = true;
            EditorUtility.SetDirty(target);
        }

        static void TextButton(Button b, Sprite bg)
        {
            if (b != null) Bg(b.GetComponent<Image>(), bg, true);
        }

        static void Badge(Text count, Sprite sprite)
        {
            if (count != null) Bg(count.transform.parent.GetComponent<Image>(), sprite, false);
        }
    }
}
