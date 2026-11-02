using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.UI
{
    /// <summary>
    /// Picture lettering. Art/Fonts holds words as PNGs (there is no font file), so a label whose word exists as a
    /// picture shows it and keeps its Text, switched off, as the fallback.
    /// </summary>
    public static class WordArt
    {
        /// <summary>Art pixel to canvas unit (the art is 668 px wide, the canvas 1080).</summary>
        public const float MockupScale = 1080f / 668f;
        public const string PictureName = "word";

        /// <summary>Canvas size of a word picture.</summary>
        public static Vector2 Size(Sprite s) => s != null ? s.rect.size * MockupScale : Vector2.zero;

        /// <summary>
        /// Shows the word picture over the text and switches the Text off. A null sprite keeps the text. Safe to call
        /// again. Returns the picture, or null.
        /// </summary>
        public static Image Picture(Text text, Sprite word)
        {
            if (text == null) return null;
            var old = text.transform.Find(PictureName);
            Image img = old != null ? old.GetComponent<Image>() : null;
            if (word == null)
            {
                text.enabled = true;
                if (img != null) img.gameObject.SetActive(false);
                return null;
            }
            if (img == null)
            {
                var go = new GameObject(PictureName, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(text.transform, false);
                img = go.GetComponent<Image>();
            }
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Size(word);
            img.sprite = word;
            img.color = Color.white;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.gameObject.SetActive(true);
            text.enabled = false;
            return img;
        }

        /// <summary>The skin's font when a TTF/OTF sits in Art/Fonts, else Unity's built-in one.</summary>
        public static Font Font
        {
            get
            {
                var skin = UISkin.Current;
                if (skin != null && skin.font != null) return skin.font;
                return _builtin != null ? _builtin : (_builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            }
        }
        static Font _builtin;

        /// <summary>
        /// Drops Bold while the skin font is set. It has no bold cut and Unity's fake bold clogs the letters.
        /// </summary>
        public static FontStyle Style(FontStyle wanted)
        {
            var skin = UISkin.Current;
            if (skin == null || skin.font == null) return wanted;
            if (wanted == FontStyle.Bold) return FontStyle.Normal;
            if (wanted == FontStyle.BoldAndItalic) return FontStyle.Italic;
            return wanted;
        }
    }
}
