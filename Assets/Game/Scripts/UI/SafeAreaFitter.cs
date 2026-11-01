using UnityEngine;

namespace BallSortGame.UI
{
    /// <summary>
    /// Keeps a UI element out of the phone's cutout, status bar and gesture bar (<see cref="Screen.safeArea"/>; the app
    /// renders behind them, see Android "Render outside safe area"). Two uses, picked from the element's anchors:
    /// a rect stretched over the screen height (anchor y 0..1, e.g. a full-screen panel) gets the insets as padding,
    /// so everything inside moves in; any other element is shifted down by the top inset when it hangs from the top
    /// edge (anchor y = 1) or up by the bottom inset when it stands on the bottom edge (anchor y = 0). Insets are in the
    /// canvas' units, so they match the <see cref="UnityEngine.UI.CanvasScaler"/>. Re-applied when the safe area or the
    /// screen size changes (rotation, split screen, foldables). Attach with <see cref="Attach"/>.
    /// </summary>
    public class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rt;
        Canvas _canvas;
        Vector2 _basePos, _baseOffsetMin, _baseOffsetMax;
        bool _captured;
        Rect _lastSafe;
        Vector2Int _lastScreen;

        /// <summary>Adds the fitter to <paramref name="target"/> (once) and applies the current safe area.</summary>
        public static SafeAreaFitter Attach(Component target)
        {
            if (target == null) return null;
            var f = target.GetComponent<SafeAreaFitter>();
            if (f == null) f = target.gameObject.AddComponent<SafeAreaFitter>();
            f.Apply();
            return f;
        }

        void OnEnable() => Apply();

        void Update()
        {
            if (Screen.safeArea != _lastSafe || Screen.width != _lastScreen.x || Screen.height != _lastScreen.y) Apply();
        }

        void Apply()
        {
            if (_rt == null) _rt = transform as RectTransform;
            if (_rt == null) return;
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (!_captured)
            {
                _basePos = _rt.anchoredPosition;
                _baseOffsetMin = _rt.offsetMin;
                _baseOffsetMax = _rt.offsetMax;
                _captured = true;
            }
            _lastSafe = Screen.safeArea;
            _lastScreen = new Vector2Int(Screen.width, Screen.height);

            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            float top = Mathf.Max(0f, Screen.height - _lastSafe.yMax) / scale;
            float bottom = Mathf.Max(0f, _lastSafe.yMin) / scale;

            bool stretched = _rt.anchorMin.y <= 0.001f && _rt.anchorMax.y >= 0.999f;
            if (stretched)
            {
                _rt.offsetMin = new Vector2(_baseOffsetMin.x, _baseOffsetMin.y + bottom);
                _rt.offsetMax = new Vector2(_baseOffsetMax.x, _baseOffsetMax.y - top);
            }
            else if (_rt.anchorMin.y >= 0.999f) _rt.anchoredPosition = new Vector2(_basePos.x, _basePos.y - top);
            else if (_rt.anchorMax.y <= 0.001f) _rt.anchoredPosition = new Vector2(_basePos.x, _basePos.y + bottom);
        }
    }
}
