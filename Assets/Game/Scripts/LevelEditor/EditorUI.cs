using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BallSortGame.LevelEditor
{
    /// <summary>
    /// Small uGUI kit for the in-app level editor. Everything is built at runtime with procedural sprites, so the
    /// editor needs no prefabs, scene objects or Resources and ships inside the normal game build.
    /// </summary>
    public static class EditorUI
    {
        public static readonly Color Background = new Color(0.09f, 0.09f, 0.13f, 1f);
        public static readonly Color Bar = new Color(0.15f, 0.15f, 0.21f, 1f);
        public static readonly Color ButtonColor = new Color(0.26f, 0.26f, 0.36f, 1f);
        public static readonly Color Accent = new Color(0.49f, 0.45f, 0.93f, 1f);
        public static readonly Color Go = new Color(0.30f, 0.72f, 0.38f, 1f);
        public static readonly Color Danger = new Color(0.80f, 0.30f, 0.30f, 1f);
        public static readonly Color Good = new Color(0.55f, 0.92f, 0.55f, 1f);
        public static readonly Color Bad = new Color(1f, 0.50f, 0.45f, 1f);
        public static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);

        static Font _font;
        static Sprite _circle, _roundRect;

        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeRounded(128, 64, 0));
        public static Sprite RoundRect => _roundRect != null ? _roundRect : (_roundRect = MakeRounded(64, 18, 22));

        /// <summary>Anti-aliased white rounded square; radius = size/2 gives a circle. border > 0 makes it 9-sliced.</summary>
        static Sprite MakeRounded(int size, float radius, int border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // signed distance to a rounded box
                float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                byte a = (byte)(Mathf.Clamp01(0.5f - outside) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        // ------------------------------------------------------------------ rects

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        /// <summary>Full-width strip at a fixed distance from the top or bottom edge.</summary>
        public static RectTransform Band(string name, Transform parent, bool fromTop, float offset, float height, float margin = 24f)
        {
            var rt = Rect(name, parent);
            float a = fromTop ? 1f : 0f;
            rt.anchorMin = new Vector2(0f, a);
            rt.anchorMax = new Vector2(1f, a);
            rt.pivot = new Vector2(0.5f, a);
            rt.sizeDelta = new Vector2(-2f * margin, height);
            rt.anchoredPosition = new Vector2(0f, fromTop ? -offset : offset);
            return rt;
        }

        /// <summary>Band laid out as a row whose children share the width by their flex weights.</summary>
        public static RectTransform Row(string name, Transform parent, bool fromTop, float offset, float height, float spacing = 14f)
        {
            var rt = Band(name, parent, fromTop, offset, height);
            RowLayout(rt.gameObject, spacing);
            return rt;
        }

        public static HorizontalLayoutGroup RowLayout(GameObject go, float spacing)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return h;
        }

        /// <summary>Width share inside a row: preferred 0, so all space is split by the weights.</summary>
        public static T Flex<T>(T c, float weight) where T : Component
        {
            if (!c.TryGetComponent(out LayoutElement le)) le = c.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            le.flexibleWidth = weight;
            return c;
        }

        // ------------------------------------------------------------------ widgets

        public static Image Image(string name, Transform parent, Sprite sprite, Color color, bool sliced = false)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            return img;
        }

        public static Image Panel(string name, Transform parent, Color color)
        {
            var img = Image(name, parent, RoundRect, color, true);
            Stretch(img.rectTransform, Vector2.zero, Vector2.zero);
            return img;
        }

        /// <summary>Text filling its parent, shrinking to fit down to a third of <paramref name="size"/>.</summary>
        public static Text Label(Transform parent, string text, int size, TextAnchor anchor, Color color, FontStyle style = FontStyle.Bold)
        {
            var rt = Stretch(Rect("text", parent), new Vector2(8f, 0f), new Vector2(-8f, 0f));
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = color;
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Max(12, size / 3);
            t.resizeTextMaxSize = size;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string label, Color color, UnityAction onClick, int fontSize = 40, float flex = 1f)
        {
            var img = Image(label, parent, RoundRect, color, true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(onClick);
            Label(img.transform, label, fontSize, TextAnchor.MiddleCenter, Color.white);
            Flex(img, flex);
            return b;
        }

        public static void SetLabel(Button b, string label)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = label;
        }

        /// <summary>"Caption  [-] value [+]" in a row. Returns the value text.</summary>
        public static Text Stepper(Transform parent, string caption, UnityAction<int> onDelta, float flex = 1f)
        {
            var box = Image(caption, parent, RoundRect, Bar, true);
            Flex(box, flex);
            var row = RowLayout(box.gameObject, 6f);
            row.padding = new RectOffset(6, 6, 6, 6);

            var cap = Flex(Rect("caption", box.transform), 1.3f);
            Label(cap, caption, 32, TextAnchor.MiddleCenter, Muted);
            Button(box.transform, "-", ButtonColor, () => onDelta(-1), 52, 0.9f);
            var val = Flex(Rect("value", box.transform), 0.9f);
            var text = Label(val, "", 44, TextAnchor.MiddleCenter, Color.white);
            Button(box.transform, "+", ButtonColor, () => onDelta(+1), 52, 0.9f);
            return text;
        }

        public static InputField Input(Transform parent, string placeholder, UnityAction<string> onEndEdit, float flex = 1f)
        {
            var bg = Image("input", parent, RoundRect, Bar, true);
            Flex(bg, flex);
            var field = bg.gameObject.AddComponent<InputField>();
            var text = Label(bg.transform, "", 40, TextAnchor.MiddleLeft, Color.white, FontStyle.Normal);
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.rectTransform.offsetMin = new Vector2(24f, 0f);
            var ph = Label(bg.transform, placeholder, 40, TextAnchor.MiddleLeft, Muted, FontStyle.Italic);
            ph.rectTransform.offsetMin = new Vector2(24f, 0f);
            field.textComponent = text;
            field.placeholder = ph;
            field.targetGraphic = bg;
            field.lineType = InputField.LineType.SingleLine;
            if (onEndEdit != null) field.onEndEdit.AddListener(onEndEdit);
            return field;
        }

        /// <summary>Vertical scroll list filling <paramref name="parent"/>; returns the content to add rows to.</summary>
        public static RectTransform ScrollList(Transform parent, Vector2 offsetMin, Vector2 offsetMax)
        {
            var view = Stretch(Rect("scroll", parent), offsetMin, offsetMax);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.15f);
            var content = Rect("content", view);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 10f;
            v.padding = new RectOffset(10, 10, 10, 10);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = content;
            sr.viewport = view;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            return content;
        }

        /// <summary>Full-width row for a scroll list.</summary>
        public static Button ListButton(Transform content, string label, Color color, UnityAction onClick, float height = 96f)
        {
            var b = Button(content, label, color, onClick, 36);
            var le = b.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            b.GetComponentInChildren<Text>().rectTransform.offsetMin = new Vector2(24f, 0f);
            return b;
        }
    }
}
