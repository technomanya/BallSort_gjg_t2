using UnityEngine;

namespace BallSortGame.UI
{
    /// <summary>
    /// Keeps a UI element out of the cutout, status bar and gesture bar, since the app renders behind them.
    /// A rect stretched over the screen height gets the insets as padding. A rect anchored to the top or bottom edge
    /// is shifted by that edge's inset.
    /// </summary>
    public class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rt;
        Canvas _canvas;
        Vector2 _basePos, _baseOffsetMin, _baseOffsetMax;
        bool _captured;
        Rect _lastSafe;
        Vector2Int _lastScreen;
        float _lastScale;

        /// <summary>Adds the fitter once and applies the current safe area.</summary>
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
            // the canvas scale can change a frame after the screen size, so watch it too
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            if (Screen.safeArea != _lastSafe || Screen.width != _lastScreen.x || Screen.height != _lastScreen.y || !Mathf.Approximately(scale, _lastScale)) Apply();
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

            _lastScale = _canvas != null ? _canvas.scaleFactor : 1f;
            float scale = _lastScale > 0f ? _lastScale : 1f;
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
