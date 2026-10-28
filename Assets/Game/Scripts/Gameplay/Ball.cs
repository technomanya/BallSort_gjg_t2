using System;
using System.Collections;
using UnityEngine;

namespace BallSortGame.Gameplay
{
    /// <summary>A single ball. Mirrors BallSort.BallData: a color index, a sprite, select-rise and path-based movement.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class Ball : MonoBehaviour
    {
        [Header("Motion feel")]
        [Tooltip("Arc bulge used when there is little room above the tubes, in world units.")]
        public float ArcHeight = 0.55f;
        [Tooltip("With at least this much free space above the lifted ball the flight becomes a half ellipse.")]
        public float MinEllipseHeadroom = 0.6f;
        [Tooltip("Ellipse height = min(headroom, EllipseHeightBase + distance * EllipseHeightPerUnit).")]
        public float EllipseHeightBase = 0.6f;
        public float EllipseHeightPerUnit = 0.22f;
        [Tooltip("Height of the little hop after the ball lands in its slot.")]
        public float BounceHeight = 0.2f;
        public float BounceDuration = 0.16f;

        public int ColorIndex { get; private set; }
        public bool IsMoving => _moving;

        SpriteRenderer _sr;
        Coroutine _move;
        bool _moving;

        void Awake() { _sr = GetComponent<SpriteRenderer>(); }

        public void Setup(int colorIndex, Sprite sprite, int sortingOrder)
        {
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            ColorIndex = colorIndex;
            _sr.sprite = sprite;
            _sr.sortingOrder = sortingOrder;
            name = $"Ball_{colorIndex}";
        }

        /// <summary>Quick ease-out rise to the stand-by position above the tube (select).</summary>
        public void RiseTo(Vector3 worldPos, float duration)
        {
            Stop();
            _move = StartCoroutine(RiseRoutine(worldPos, duration));
        }

        IEnumerator RiseRoutine(Vector3 to, float duration)
        {
            _moving = true;
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
            _moving = false;
            _move = null;
        }

        /// <summary>
        /// Full move: rise out of the source tube to liftFrom (skipped when already there), arc across to liftTo,
        /// drop into the slot, then a little bounce. Speed is in world units per second.
        /// </summary>
        public void FlyTo(Vector3 liftFrom, Vector3 liftTo, Vector3 slot, float speed, float delay, float arcHeadroom, Action onDone)
        {
            Stop();
            _move = StartCoroutine(FlyRoutine(liftFrom, liftTo, slot, speed, delay, arcHeadroom, onDone));
        }

        IEnumerator FlyRoutine(Vector3 liftFrom, Vector3 liftTo, Vector3 slot, float speed, float delay, float arcHeadroom, Action onDone)
        {
            _moving = true;
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
                    // half ellipse over the chord liftFrom -> liftTo: horizontal semi-axis = across/2, vertical = height
                    float height = Mathf.Min(arcHeadroom, EllipseHeightBase + across * EllipseHeightPerUnit);
                    Vector3 mid = (liftFrom + liftTo) * 0.5f;
                    Vector3 half = (liftTo - liftFrom) * 0.5f;
                    // arc length of the half ellipse (Ramanujan) sets the duration at the given speed
                    float a = across * 0.5f, bAxis = height;
                    float h = ((a - bAxis) * (a - bAxis)) / ((a + bAxis) * (a + bAxis));
                    float perimeter = Mathf.PI * (a + bAxis) * (1f + 3f * h / (10f + Mathf.Sqrt(4f - 3f * h)));
                    float dur = Mathf.Max(0.08f, (perimeter * 0.5f) / speed);
                    float t = 0f;
                    while (t < dur)
                    {
                        t += Time.deltaTime;
                        float k = Mathf.Clamp01(t / dur);
                        k = k * k * (3f - 2f * k); // smoothstep on the angle: gentle leave and arrive
                        float theta = Mathf.PI * (1f - k);            // pi at start, 0 at the end
                        // theta = pi -> liftFrom (cos = -1), theta = 0 -> liftTo (cos = +1)
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

            _moving = false;
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
            _moving = false;
        }
    }
}
