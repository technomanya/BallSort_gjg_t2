using System;
using System.Collections;
using UnityEngine;

namespace BallSortGame.Gameplay
{
    /// <summary>A single ball: a colour index, a sprite, and the select and move animations.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Ball : MonoBehaviour
    {
        [Header("Motion feel")]
        [Tooltip("Arc bulge when there is little room above the tubes.")]
        public float ArcHeight = 0.55f;
        [Tooltip("Free space above needed to fly a half ellipse.")]
        public float MinEllipseHeadroom = 0.6f;
        [Tooltip("Ellipse height = base + distance * per unit.")]
        public float EllipseHeightBase = 0.6f;
        public float EllipseHeightPerUnit = 0.22f;
        [Tooltip("Height of the hop after landing.")]
        public float BounceHeight = 0.2f;
        public float BounceDuration = 0.16f;

        public int ColorIndex { get; private set; }
        /// <summary>
        /// Cannonball obstacle. It moves alone and only into an empty tube, so it sinks to the bottom.
        /// </summary>
        public bool IsHeavy { get; private set; }
        /// <summary>Shown as a grey "?" until Reveal.</summary>
        public bool IsHidden { get; private set; }

        static readonly Color HiddenTint = new Color(0.42f, 0.42f, 0.50f, 1f);

        SpriteRenderer _sr;
        Coroutine _move;
        Sprite _realSprite;
        Color _realColor = Color.white;
        GameObject _mark;
        SpriteRenderer _weight;

        void Awake() { _sr = GetComponent<SpriteRenderer>(); }

        public void Setup(int colorIndex, Sprite sprite, int sortingOrder)
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            ColorIndex = colorIndex;
            _sr.sprite = sprite;
            _sr.sortingOrder = sortingOrder;
            name = $"Ball_{colorIndex}";
        }

        /// <summary>Draws the weight over the colour. The colour still counts for every rule but the move rule.</summary>
        public void SetHeavy(bool heavy, Sprite weight)
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            IsHeavy = heavy;
            if (!heavy)
            {
                if (_weight != null) _weight.gameObject.SetActive(false);
                return;
            }
            if (_weight == null)
            {
                var go = new GameObject("weight", typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                _weight = go.GetComponent<SpriteRenderer>();
            }
            _weight.gameObject.SetActive(true);
            _weight.sprite = weight;
            _weight.color = Color.white;
            _weight.sortingOrder = _sr.sortingOrder + 1;   // over the colour, under the "?" mark
            name = $"Ball_{ColorIndex}_heavy";
        }

        /// <summary>Shows a grey "?" ball. The colour index stays, so the rules still know it.</summary>
        public void Hide(Sprite placeholder)
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            if (!IsHidden)
            {
                _realSprite = _sr.sprite;
                _realColor = _sr.color;
            }
            IsHidden = true;
            if (placeholder != null) _sr.sprite = placeholder;
            _sr.color = HiddenTint;
            EnsureMark();
        }

        /// <summary>Level editor preview: keeps the colour but draws the "?". The ball does not count as hidden.</summary>
        public void ShowHiddenMark()
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            EnsureMark();
        }

        void EnsureMark()
        {
            if (_mark == null)
            {
                _mark = new GameObject("mark", typeof(MeshRenderer), typeof(TextMesh));
                _mark.transform.SetParent(transform, false);
                var tm = _mark.GetComponent<TextMesh>();
                tm.font = BallSortGame.UI.WordArt.Font;
                tm.text = "?";
                tm.fontSize = 64;
                tm.fontStyle = BallSortGame.UI.WordArt.Style(FontStyle.Bold);
                tm.characterSize = 0.11f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.white;
                var mr = _mark.GetComponent<MeshRenderer>();
                mr.sharedMaterial = tm.font.material;
                mr.sortingOrder = _sr.sortingOrder + 2;   // above the glass
            }
        }

        public void Reveal()
        {
            if (!IsHidden) return;
            IsHidden = false;
            if (_realSprite != null) _sr.sprite = _realSprite;
            _sr.color = _realColor;
            if (_mark != null) { Destroy(_mark); _mark = null; }
        }

        /// <summary>Quick rise above the tube when selected.</summary>
        public void RiseTo(Vector3 worldPos, float duration)
        {
            Stop();
            _move = StartCoroutine(RiseRoutine(worldPos, duration));
        }

        IEnumerator RiseRoutine(Vector3 to, float duration)
        {
            Vector3 from = transform.position;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                k = 1f - (1f - k) * (1f - k) * (1f - k); // ease out cubic
                transform.position = Vector3.LerpUnclamped(from, to, k);
                yield return null;
            }
            transform.position = to;
            _move = null;
        }

        /// <summary>
        /// Rise to liftFrom, arc across to liftTo, drop into the slot, then a little bounce. Speed is world units per second.
        /// </summary>
        public void FlyTo(Vector3 liftFrom, Vector3 liftTo, Vector3 slot, float speed, float delay, float arcHeadroom, Action onDone)
        {
            Stop();
            _move = StartCoroutine(FlyRoutine(liftFrom, liftTo, slot, speed, delay, arcHeadroom, onDone));
        }

        IEnumerator FlyRoutine(Vector3 liftFrom, Vector3 liftTo, Vector3 slot, float speed, float delay, float arcHeadroom, Action onDone)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);

            // 1) rise out of the source tube (ease out)
            Vector3 start = transform.position;
            float rise = Vector3.Distance(start, liftFrom);
            if (rise > 0.01f)
            {
                float dur = Mathf.Max(0.04f, rise / (speed * 1.3f));
                float t = 0f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / dur);
                    k = 1f - (1f - k) * (1f - k);
                    transform.position = Vector3.Lerp(start, liftFrom, k);
                    yield return null;
                }
            }

            // 2) cross to the target tube
            float across = Vector3.Distance(liftFrom, liftTo);
            if (across > 0.01f)
            {
                if (arcHeadroom >= MinEllipseHeadroom)
                {
                    // half ellipse over the chord liftFrom -> liftTo
                    float height = Mathf.Min(arcHeadroom, EllipseHeightBase + across * EllipseHeightPerUnit);
                    Vector3 mid = (liftFrom + liftTo) * 0.5f;
                    Vector3 half = (liftTo - liftFrom) * 0.5f;
                    // Ramanujan's arc length, so the duration matches the speed
                    float a = across * 0.5f, bAxis = height;
                    float h = ((a - bAxis) * (a - bAxis)) / ((a + bAxis) * (a + bAxis));
                    float perimeter = Mathf.PI * (a + bAxis) * (1f + 3f * h / (10f + Mathf.Sqrt(4f - 3f * h)));
                    float dur = Mathf.Max(0.08f, (perimeter * 0.5f) / speed);
                    float t = 0f;
                    while (t < dur)
                    {
                        t += Time.deltaTime;
                        float k = Mathf.Clamp01(t / dur);
                        k = k * k * (3f - 2f * k); // smoothstep: gentle leave and arrive
                        float theta = Mathf.PI * (1f - k);            // pi at liftFrom, 0 at liftTo
                        transform.position = mid + half * Mathf.Cos(theta) + Vector3.up * (height * Mathf.Sin(theta));
                        yield return null;
                    }
                }
                else
                {
                    // low ceiling: shallow quadratic bezier that stays inside the available room
                    float bulge = Mathf.Min(ArcHeight + across * 0.06f, Mathf.Max(0.05f, arcHeadroom));
                    Vector3 ctrl = (liftFrom + liftTo) * 0.5f + Vector3.up * bulge;
                    float dur = Mathf.Max(0.06f, across / speed);
                    float t = 0f;
                    while (t < dur)
                    {
                        t += Time.deltaTime;
                        float k = Mathf.Clamp01(t / dur);
                        k = k * k * (3f - 2f * k); // smoothstep
                        Vector3 p0 = Vector3.Lerp(liftFrom, ctrl, k);
                        Vector3 p1 = Vector3.Lerp(ctrl, liftTo, k);
                        transform.position = Vector3.Lerp(p0, p1, k);
                        yield return null;
                    }
                }
            }
            transform.position = liftTo;

            // 3) drop into the slot (ease in, gravity-like)
            {
                float drop = Vector3.Distance(liftTo, slot);
                float dur = Mathf.Max(0.05f, drop / (speed * 1.25f));
                float t = 0f;
                while (t < dur)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / dur);
                    k = k * k;
                    transform.position = Vector3.Lerp(liftTo, slot, k);
                    yield return null;
                }
                transform.position = slot;
            }

            // 4) little hop before settling
            if (BounceHeight > 0f && BounceDuration > 0f)
            {
                float t = 0f;
                while (t < BounceDuration)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / BounceDuration);
                    float h = Mathf.Sin(k * Mathf.PI) * BounceHeight;
                    transform.position = slot + Vector3.up * h;
                    yield return null;
                }
                transform.position = slot;
            }

            _move = null;
            onDone?.Invoke();
        }

        public void SnapTo(Vector3 worldPos)
        {
            Stop();
            transform.position = worldPos;
        }

        void Stop()
        {
            if (_move != null) { StopCoroutine(_move); _move = null; }
        }
    }
}
