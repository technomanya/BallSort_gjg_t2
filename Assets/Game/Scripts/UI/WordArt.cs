using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.UI
{
    /// <summary>
    /// Picture lettering. Art/Fonts holds the mockup's words as 1x PNG exports (there is no font file), so a label
    /// whose word exists as a picture shows that picture and keeps its Text, switched off, as the fallback. The
    /// pictures are drawn at mockup pixels x <see cref="MockupScale"/>, like every other size of the 1080x2340 layout.
    /// Numbers are drawn by <see cref="WordLabel"/> from the digit pictures of the skin.
    /// </summary>
    public static class WordArt
    {
        /// <summary>Mockup pixel to canvas unit (the mockups are 668 px wide, the canvas 1080).</summary>
        public const float MockupScale = 1080f / 668f;
        public const string PictureName = "word";

        /// <summary>Canvas size of a word picture.</summary>
        public static Vector2 Size(Sprite s) => s != null ? s.rect.size * MockupScale : Vector2.zero;

        /// <summary>
        /// Shows <paramref name="word"/> in place of <paramref name="text"/>: a "word" Image child centred on the text's
        /// rect, the Text component switched off. With a null sprite the text stays as it is. Safe to call again: the
        /// existing picture is reused. Returns the picture, or null.
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
        /// Font style to use for <paramref name="wanted"/>: the skin font is a display face with no bold cut (Unity
        /// would fake it and clog the counters), so Bold becomes Normal and BoldAndItalic becomes Italic while it is set.
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
