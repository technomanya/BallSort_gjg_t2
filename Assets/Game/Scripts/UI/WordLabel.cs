using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.UI
{
    /// <summary>
    /// "LEVEL 12" drawn with pictures: the LEVEL word of the skin and one picture per digit (<see cref="UISkin.digits"/>)
    /// in a horizontal row; while a digit picture is missing (the kit ships only a few) the number falls back to the
    /// plain Text. <see cref="GameHUD"/> and <see cref="MainMenuView"/> write through <see cref="Show"/>, so a scene
    /// without the row keeps working. Built by the scene builder (BallSortSceneBuilder.LevelLabel).
    /// </summary>
    [DisallowMultipleComponent]
    public class WordLabel : MonoBehaviour
    {
        [Tooltip("Row (HorizontalLayoutGroup) holding the word, the digit pictures and the fallback text.")]
        public RectTransform row;
        [Tooltip("Leading word picture (LEVEL); optional.")]
        public Image word;
        [Tooltip("Plain text shown while a digit picture is missing.")]
        public Text fallback;
        [Tooltip("Height of a digit picture in canvas units (the word's cap height).")]
        public float digitHeight = 37f;

        readonly List<Image> _digits = new List<Image>();

        /// <summary>Writes <paramref name="text"/> / <paramref name="number"/> through the WordLabel above <paramref name="fallback"/>, else into the text.</summary>
        public static void Show(Text fallback, string text, int number)
        {
            var label = fallback != null ? fallback.GetComponentInParent<WordLabel>(true) : null;
            if (label != null) label.Set(text, number);
            else if (fallback != null) fallback.text = text;
        }

        /// <summary>Shows the word picture plus <paramref name="number"/> in digit pictures, or <paramref name="text"/> (the number alone when the word picture exists).</summary>
        public void Set(string text, int number)
        {
            Collect();
            bool hasWord = word != null && word.sprite != null;
            if (word != null) word.gameObject.SetActive(hasWord);

            var skin = UISkin.Current;
            Sprite[] digits = skin != null ? skin.digits : null;
            string s = number.ToString();
            bool hasDigits = digits != null && number >= 0;
            if (hasDigits)
                foreach (char c in s)
                    if (c - '0' >= digits.Length || digits[c - '0'] == null) { hasDigits = false; break; }

            for (int i = 0; hasDigits && i < s.Length; i++)
            {
                var img = Digit(i);
                img.sprite = digits[s[i] - '0'];
                img.rectTransform.sizeDelta = new Vector2(digitHeight * img.sprite.rect.width / img.sprite.rect.height, digitHeight);
                img.gameObject.SetActive(true);
            }
            for (int i = hasDigits ? s.Length : 0; i < _digits.Count; i++) _digits[i].gameObject.SetActive(false);

            if (fallback == null) return;
            fallback.gameObject.SetActive(!hasDigits);
            if (hasDigits) return;
            fallback.text = hasWord ? s : text;
            fallback.rectTransform.sizeDelta = new Vector2(fallback.preferredWidth + 4f, Mathf.Max(digitHeight, fallback.preferredHeight));
        }

        /// <summary>Digit pictures already under the row (saved with the scene, or made before a domain reload) are reused, not duplicated.</summary>
        void Collect()
        {
            if (_digits.Count > 0) return;
            var parent = row != null ? row : (RectTransform)transform;
            foreach (Transform c in parent)
                if (c.name.StartsWith("digit_") && c.TryGetComponent(out Image img)) _digits.Add(img);
        }

        Image Digit(int i)
        {
            while (_digits.Count <= i)
            {
                var go = new GameObject("digit_" + _digits.Count, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(row != null ? row : transform, false);
                go.transform.SetSiblingIndex((word != null ? 1 : 0) + _digits.Count);   // after the word, before the text
                var img = go.GetComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                _digits.Add(img);
            }
            return _digits[i];
        }
    }
}
