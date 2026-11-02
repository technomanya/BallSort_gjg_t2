using UnityEngine;

namespace BallSortGame.UI
{
    /// <summary>
    /// Scales a fixed-size picture and its children so it covers its parent, like CSS "cover", cropping the overflow.
    /// The home page garden uses it so the pots stay on the soil at every screen aspect.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class CoverFitter : MonoBehaviour
    {
        [Tooltip("The picture's own pixel size. Children use these units.")]
        public Vector2 size = new Vector2(668f, 1080f);

        Vector2 _lastParent;

        void OnEnable() => Fit(true);

        void Update() => Fit(false);

        void OnTransformParentChanged() => Fit(true);

        void Fit(bool force)
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            var r = parent.rect;
            if (r.width <= 0f || r.height <= 0f || size.x <= 0f || size.y <= 0f) return;
            if (!force && r.size == _lastParent && rt.sizeDelta == size) return;
            _lastParent = r.size;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            float k = Mathf.Max(r.width / size.x, r.height / size.y);
            rt.localScale = new Vector3(k, k, 1f);
        }
    }
}
