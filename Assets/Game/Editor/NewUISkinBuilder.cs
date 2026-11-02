using System.Collections.Generic;
using System.IO;
using BallSortGame.UI;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// Re-imports the UI kit, garden and word-art sprites and rebuilds Resources/UISkin.asset. Does not touch the scene.
    /// </summary>
    public static class NewUISkinBuilder
    {
        const string Dir = "Assets/Game/Art/UI/NewUI/";
        const string BackgroundDir = "Assets/Game/Art/Backgrounds/";
        const string GardenDir = "Assets/Game/Art/Garden/";
        internal const string SkinPath = "Assets/Game/Resources/UISkin.asset";
        const string FontsDir = "Assets/Game/Art/Fonts/";

        // Per file: crop rect (null = whole picture) and 9-slice border (left, bottom, right, top).
        // Gold-framed panels have a sparkle mid-side, so stretch them sideways only.
        // Stadium shapes use half their height as border so the ends stay round.
        static readonly Dictionary<string, (RectInt? crop, Vector4 border)> Slices = new Dictionary<string, (RectInt?, Vector4)>
        {
            { "Button rectangle.PNG", (null, new Vector4(40, 40, 40, 40)) },
            { "Button rectangle hard level.PNG", (null, new Vector4(40, 40, 40, 40)) },
            { "Button square.PNG", (null, new Vector4(45, 45, 45, 45)) },
            { "Square frame.PNG", (null, new Vector4(50, 50, 50, 50)) },
            { "Button elipse.PNG", (null, new Vector4(40, 38, 40, 38)) },
            { "Big pop up shaded.PNG", (null, new Vector4(70, 70, 70, 70)) },
            { "Small pop up shaded.PNG", (null, new Vector4(60, 60, 60, 60)) },
            { "small pop up FLAT shade.png", (null, new Vector4(40, 40, 40, 40)) },
            { "Settings background.png", (null, new Vector4(24, 24, 24, 24)) },
            { "Reward row.png", (null, new Vector4(29, 28, 29, 28)) },
            { "Reward tile back.png", (null, new Vector4(20, 20, 20, 20)) },
        };

        [MenuItem("BallSort/Rebuild UI Skin")]
        public static void Apply() => BuildSkin();

        public static UISkin BuildSkin()
        {
            ConfigureSlices();
            ConfigureGardenArt();
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
            skin.squareFrame = S("Square frame.PNG");
            skin.greenCircle = S("green dot.png");
            skin.buttonPill = S("Button elipse.PNG");
            skin.popupPanel = S("Big pop up shaded.PNG");
            skin.pill = S("Small pop up shaded.PNG");
            skin.overlay = S("Background overlay (pop up ciktiginda ekranin kararmasi icin).png", BackgroundDir);
            skin.rewardStar = S("reward star.PNG");
            skin.rewardPotion = S("reward potion.PNG");
            skin.rewardCrystal = S("reward crystal.PNG");
            skin.rewardSeed = S("Seed plant reward.png");
            skin.rewardRow = S("Reward row.png");
            skin.rewardGlow = S("Reward glow.png");
            skin.cardBackFound = S("Reward tile back.png");
            skin.iconPlant = S("plant.png");
            skin.coin = S("Coin.PNG");
            skin.logo = S("Logo alone.PNG");
            skin.background = S("Background.png", BackgroundDir);
            skin.pot = S("pot.png", GardenDir);
            skin.soil = S("soil.png", GardenDir);
            skin.flowers = new Sprite[FlowerNames.Length];
            skin.flowersLit = new Sprite[FlowerNames.Length];
            for (int i = 0; i < FlowerNames.Length; i++)
            {
                skin.flowers[i] = S("flower_" + FlowerNames[i] + ".png", GardenDir);
                skin.flowersLit[i] = S("flower_" + FlowerNames[i] + "_lit.png", GardenDir);
            }
            skin.iconClose = S("exit.png");
            skin.iconSettings = S("settings.png");
            skin.iconRestart = S("restrat.png");
            skin.iconUndo = S("undo (1).png");
            skin.iconCheck = S("check mark.png");
            skin.iconArrow = S("Arrow up and down.png");
            ConfigureWordArt();
            skin.font = FindFont();
            skin.wordContinue = Word("CONTINUE.png");
            skin.wordHome = Word("Home (1).png");
            skin.wordTryAgain = Word("Try Again.png");
            skin.wordWellDone = Word("WELL DONE!.png");
            skin.wordOutOfMoves = Word("OUT OF MOVES !.png");
            skin.wordManaFlip = Word("Mana Flip.png");
            skin.wordManaGarden = Word("Mana Garden.png");
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssets();
            return skin;
        }

        // Each Art/Fonts picture becomes one uncompressed sprite (Unity auto-slices them into letters otherwise).
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
                var wanted = new[] { (name: Path.GetFileNameWithoutExtension(file), rect: new RectInt(0, 0, w, h)) };
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
                    rects[i] = i < old.Length ? old[i] : new SpriteRect { spriteID = GUID.Generate() };   // keep ids so references survive
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

        // Named sprite of an Art/Fonts picture, or its first one when name is null.
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

        // First font file under Art/Fonts, or null (the folder usually holds pictures only).
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

        // Each kit texture becomes one whole-picture sprite (auto-slicing cut some icons into pieces), cropped and
        // bordered per Slices. The first sprite id is kept so references survive.
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

        // Mana Garden slot order. Files are flower_<name>.png and flower_<name>_lit.png.
        static readonly string[] FlowerNames = { "tulip", "rose", "lily", "bellflower", "daisy" };

        // Stem foot x as a fraction of width, used as the pivot so each flower stands in the soil.
        static readonly Dictionary<string, float> StemX = new Dictionary<string, float>
        {
            { "tulip", 0.541f }, { "rose", 0.540f }, { "lily", 0.610f }, { "bellflower", 0.460f }, { "daisy", 0.405f },
        };

        // Art/Garden: one sprite per picture, no mipmaps, flowers pivoted at the stem foot.
        static void ConfigureGardenArt()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { GardenDir.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                var pivot = new Vector2(0.5f, 0.5f);
                bool custom = false;
                foreach (var kv in StemX)
                    if (name == "flower_" + kv.Key || name == "flower_" + kv.Key + "_lit") { pivot = new Vector2(kv.Value, 0f); custom = true; }
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                bool same = settings.textureType == TextureImporterType.Sprite && settings.spriteMode == (int)SpriteImportMode.Single && !settings.mipmapEnabled
                    && settings.alphaIsTransparency && settings.spriteAlignment == (int)(custom ? SpriteAlignment.Custom : SpriteAlignment.Center)
                    && (!custom || (settings.spritePivot - pivot).sqrMagnitude < 1e-6f) && importer.maxTextureSize >= 2048;
                if (same) continue;
                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spritePixelsPerUnit = 100;
                settings.mipmapEnabled = false;
                settings.alphaIsTransparency = true;
                settings.wrapMode = TextureWrapMode.Clamp;
                settings.filterMode = FilterMode.Bilinear;
                settings.spriteAlignment = (int)(custom ? SpriteAlignment.Custom : SpriteAlignment.Center);
                settings.spritePivot = pivot;
                importer.SetTextureSettings(settings);
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
        }

        // Size from the PNG header, since sprite rects are in source pixels.
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
    }
}
