using BallSortGame.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BallSortGame.LevelEditor
{
    /// <summary>
    /// Small uGUI kit for UI built in code at runtime. Procedural sprites stand in when the game skin lacks one,
    /// so it needs no prefabs or scene objects.
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

        static Sprite _circle, _roundRect;

        public static Font Font => WordArt.Font;
        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeRounded(128, 64, 0));
        public static Sprite RoundRect => _roundRect != null ? _roundRect : (_roundRect = MakeRounded(64, 18, 22));

        // --- game skin sprites, with procedural fallbacks

        public static UISkin Skin => UISkin.Current;
        static Sprite Pick(Sprite skinSprite, Sprite fallback) => skinSprite != null ? skinSprite : fallback;
        /// <summary>Dark HUD pill panel (booster bar, popups).</summary>
        public static Sprite PillSprite => Pick(Skin?.pill, RoundRect);
        public static Sprite SquareSprite => Pick(Skin?.buttonSquare, RoundRect);
        public static Sprite RectSprite => Pick(Skin?.buttonRect, RoundRect);
        public static Sprite GreenSprite => Pick(Skin?.buttonRectGreen, RoundRect);
        public static Sprite CircleSprite => Pick(Skin?.buttonCircle, Circle);
        public static Sprite FrameSprite => Pick(Skin?.squareFrame, RoundRect);
        /// <summary>White when the skin sprite has its own colours, else the fallback tint.</summary>
        public static Color Tint(Sprite skinSprite, Color fallback) => skinSprite != null ? Color.white : fallback;

        /// <summary>Drop shadow like the HUD texts.</summary>
        public static T Shadowed<T>(T graphic) where T : Graphic
        {
            var s = graphic.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.6f);
            s.effectDistance = new Vector2(0f, -4f);
            return graphic;
        }

        /// <summary>Fixed width inside a row.</summary>
        public static T Fixed<T>(T c, float width) where T : Component
        {
            if (!c.TryGetComponent(out LayoutElement le)) le = c.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            le.flexibleWidth = 0f;
            return c;
        }

        /// <summary>Square HUD-style button with an icon, or a text when the icon is missing.</summary>
        public static Button IconButton(Transform parent, string name, Sprite icon, string fallbackText, UnityAction onClick, float size = 110f)
        {
            var img = Image(name, parent, SquareSprite, Tint(Skin?.buttonSquare, ButtonColor), true);
            Fixed(img, size);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(onClick);
            if (icon != null)
            {
                var ic = Image("icon", img.transform, icon, Color.white);
                ic.rectTransform.sizeDelta = new Vector2(size * 0.66f, size * 0.66f);
                ic.rectTransform.anchoredPosition = new Vector2(0f, size * 0.03f);
                ic.preserveAspect = true;
                ic.raycastTarget = false;
            }
            else Shadowed(Label(img.transform, fallbackText, Mathf.RoundToInt(size * 0.3f), TextAnchor.MiddleCenter, Color.white));
            return b;
        }

        /// <summary>Rounded HUD-style text button, green or plain.</summary>
        public static Button PillButton(Transform parent, string label, bool green, UnityAction onClick, float width, int fontSize = 40)
        {
            var sprite = green ? GreenSprite : RectSprite;
            var img = Image(label, parent, sprite, Tint(green ? Skin?.buttonRectGreen : Skin?.buttonRect, green ? Go : ButtonColor), true);
            Fixed(img, width);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var colors = b.colors;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(onClick);
            var t = Shadowed(Label(img.transform, label, fontSize, TextAnchor.MiddleCenter, Color.white));
            t.rectTransform.offsetMin = new Vector2(16f, 0f);
            t.rectTransform.offsetMax = new Vector2(-16f, 0f);
            return b;
        }

        /// <summary>"CAPTION (-) value (+)" in a dark pill. Returns the value text.</summary>
        public static Text SkinStepper(Transform parent, string caption, UnityAction<int> onDelta, float width)
        {
            var box = Image(caption, parent, PillSprite, Tint(Skin?.pill, Bar), true);
            Fixed(box, width);
            var row = RowLayout(box.gameObject, 4f);
            row.padding = new RectOffset(14, 14, 14, 14);
            var cap = Flex(Rect("caption", box.transform), 1f);
            Shadowed(Label(cap, caption, 26, TextAnchor.MiddleCenter, Color.white));
            RoundButton(box.transform, "-", () => onDelta(-1));
            var val = Fixed(Rect("value", box.transform), 56f);
            var text = Shadowed(Label(val, "", 40, TextAnchor.MiddleCenter, Color.white));
            RoundButton(box.transform, "+", () => onDelta(+1));
            return text;
        }

        static Button RoundButton(Transform parent, string label, UnityAction onClick)
        {
            var img = Image(label, parent, CircleSprite, Tint(Skin?.buttonCircle, ButtonColor));
            img.preserveAspect = true;
            Fixed(img, 68f);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            var t = Shadowed(Label(img.transform, label, 48, TextAnchor.MiddleCenter, Color.white));
            t.rectTransform.anchoredPosition = new Vector2(0f, 3f);
            return b;
        }

        /// <summary>Anti-aliased white rounded square. radius = size/2 gives a circle, border > 0 makes it 9-sliced.</summary>
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

        // --- rects

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

        // --- widgets

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
            t.fontStyle = WordArt.Style(style);
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

        /// <summary>Vertical scroll list filling the parent. Returns the content to add rows to.</summary>
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
