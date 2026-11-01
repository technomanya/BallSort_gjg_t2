using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.Gameplay
{
    /// <summary>
    /// Background stars: every few seconds one rises from below the bottom edge to above the top edge, swaying,
    /// turning and fading in and out. Bigger stars are brighter and faster, so the field has depth.
    /// Under a Canvas the stars are Images spread over this RectTransform; anywhere else they are sprites spread
    /// over the camera view, drawn between the background and the board.
    /// </summary>
    public class RisingStars : MonoBehaviour
    {
        class Star
        {
            public Transform Tr;
            public SpriteRenderer Sr;
            public Image Img;
            public float X, Y, Size, Alpha, Speed, Age, SwayPhase, SwayFreq, Spin, Angle;
            public float StartY => -Size;
            public float EndY => 1f + Size;
        }

        [Tooltip("Picked at random for each star.")]
        public Sprite[] sprites;

        [Header("Timing")]
        [Tooltip("Seconds between two stars, random in this range.")]
        public Vector2 spawnInterval = new Vector2(2.5f, 5f);
        [Tooltip("Seconds to cross the screen for the smallest (x) and the biggest (y) stars.")]
        public Vector2 riseSeconds = new Vector2(16f, 9f);
        public int maxStars = 6;
        [Tooltip("Stars already on their way when the view opens, so it never starts empty.")]
        public int prewarm = 2;

        [Header("Look")]
        [Tooltip("Star height as a fraction of the view height, smallest to biggest.")]
        public Vector2 sizeRange = new Vector2(0.018f, 0.045f);
        [Tooltip("Opacity of the smallest (x) and the biggest (y) stars.")]
        public Vector2 alphaRange = new Vector2(0.25f, 0.75f);
        public Color tint = Color.white;
        [Tooltip("Side-to-side drift as a fraction of the view width.")]
        public float sway = 0.025f;
        [Tooltip("Highest turn speed in degrees per second.")]
        public float spin = 30f;
        [Tooltip("Part of the trip spent fading in at the bottom and out at the top.")]
        [Range(0f, 0.5f)] public float fade = 0.2f;

        [Header("World mode (no Canvas)")]
        public Camera cam;
        [Tooltip("Above the background (-10), below the tubes and balls.")]
        public int sortingOrder = -9;
        public float z = 9f;

        readonly List<Star> _live = new List<Star>();
        readonly Stack<Star> _pool = new Stack<Star>();
        bool _ui;
        bool _prewarmed;
        float _next;

        void Awake()
        {
            _ui = transform is RectTransform && GetComponentInParent<Canvas>() != null;
            if (!_ui && cam == null) cam = Camera.main;
        }

        void OnEnable()
        {
            if (_prewarmed) return;
            _prewarmed = true;
            for (int i = 0; i < prewarm; i++) Spawn(Random.Range(0.1f, 0.85f));
            _next = Random.Range(spawnInterval.x, spawnInterval.y);
        }

        void Update()
        {
            if (sprites == null || sprites.Length == 0) return;
            float dt = Time.unscaledDeltaTime;   // keep drifting behind pause popups

            _next -= dt;
            if (_next <= 0f)
            {
                _next = Random.Range(spawnInterval.x, spawnInterval.y);
                if (_live.Count < maxStars) Spawn(null);
            }

            if (!TryGetArea(out Rect area)) return;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var s = _live[i];
                s.Age += dt;
                s.Y += s.Speed * dt;
                if (s.Y >= s.EndY)
                {
                    Recycle(i);
                    continue;
                }
                s.Angle += s.Spin * dt;
                Place(s, area);
            }
        }

        /// <summary>The view the stars cross: the RectTransform's rect (UI) or the camera view in world units.</summary>
        bool TryGetArea(out Rect area)
        {
            if (_ui)
            {
                area = ((RectTransform)transform).rect;
                return area.height > 0f;
            }
            area = default;
            if (cam == null) return false;
            float h = cam.orthographicSize * 2f;
            float w = h * cam.aspect;
            Vector3 c = cam.transform.position;
            area = new Rect(c.x - w / 2f, c.y - h / 2f, w, h);
            return true;
        }

        void Place(Star s, Rect area)
        {
            float x = s.X + sway * Mathf.Sin(s.SwayPhase + s.Age * s.SwayFreq);
            float height = s.Size * area.height;
            float trip = Mathf.InverseLerp(s.StartY, s.EndY, s.Y);
            float k = fade > 0f ? Mathf.Clamp01(Mathf.Min(trip, 1f - trip) / fade) : 1f;
            var color = new Color(tint.r, tint.g, tint.b, tint.a * s.Alpha * k);
            var rot = Quaternion.Euler(0f, 0f, s.Angle);

            if (_ui)
            {
                var rt = (RectTransform)s.Tr;
                var r = s.Img.sprite.rect;
                rt.anchoredPosition = new Vector2(x * area.width, s.Y * area.height);
                rt.sizeDelta = new Vector2(height * r.width / r.height, height);
                rt.localRotation = rot;
                s.Img.color = color;
            }
            else
            {
                s.Tr.position = new Vector3(area.xMin + x * area.width, area.yMin + s.Y * area.height, z);
                float scale = height / s.Sr.sprite.bounds.size.y / Mathf.Max(1e-4f, transform.lossyScale.y);
                s.Tr.localScale = new Vector3(scale, scale, 1f);
                s.Tr.rotation = rot;
                s.Sr.color = color;
            }
        }

        /// <summary>Starts a star at height <paramref name="y"/> (0 = bottom edge, 1 = top edge), or just below the bottom when null.</summary>
        void Spawn(float? y)
        {
            if (sprites == null || sprites.Length == 0) return;
            var s = _pool.Count > 0 ? _pool.Pop() : Create();
            var sprite = sprites[Random.Range(0, sprites.Length)];
            if (_ui) s.Img.sprite = sprite; else s.Sr.sprite = sprite;

            float depth = Random.value;   // 0 = small, dim and slow; 1 = big, bright and fast
            s.Size = Mathf.Lerp(sizeRange.x, sizeRange.y, depth);
            s.Alpha = Mathf.Lerp(alphaRange.x, alphaRange.y, depth);
            s.Speed = (s.EndY - s.StartY) / Mathf.Max(0.1f, Mathf.Lerp(riseSeconds.x, riseSeconds.y, depth));
            s.X = Random.Range(0.06f, 0.94f);
            s.Y = y ?? s.StartY;
            s.Age = 0f;
            s.SwayPhase = Random.Range(0f, Mathf.PI * 2f);
            s.SwayFreq = Random.Range(0.4f, 0.9f);
            s.Spin = Random.Range(-spin, spin);
            s.Angle = Random.Range(0f, 360f);
            s.Tr.gameObject.SetActive(true);
            _live.Add(s);
            if (TryGetArea(out Rect area)) Place(s, area);
        }

        Star Create()
        {
            var s = new Star();
            if (_ui)
            {
                var go = new GameObject("star", typeof(RectTransform), typeof(Image));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                s.Img = go.GetComponent<Image>();
                s.Img.raycastTarget = false;
                s.Tr = rt;
            }
            else
            {
                var go = new GameObject("star", typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                s.Sr = go.GetComponent<SpriteRenderer>();
                s.Sr.sortingOrder = sortingOrder;
                s.Tr = go.transform;
            }
            return s;
        }

        void Recycle(int index)
        {
            var s = _live[index];
            _live.RemoveAt(index);
            s.Tr.gameObject.SetActive(false);
            _pool.Push(s);
        }
    }
}
