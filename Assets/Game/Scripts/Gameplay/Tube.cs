using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BallSortGame.Gameplay
{
    /// <summary>
    /// A tube holding a stack of balls. Mirrors BallSort.TubeData:
    /// Balls (bottom-first), Capacity, TopBall, TopBallSameCount, IsCompleted, CanImport, Import, OnSelect/CancelSelect.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D))]
    public class Tube : MonoBehaviour
    {
        [Header("Skin (set by GameMgr)")]
        public float TubeWidth = 1.81f;
        public float TubeHeight = 4.8f;
        public float FirstBallY = -1.79f;   // local y of the bottom ball center
        public float BallPitch = 1.08f;     // distance between stacked ball centers
        public float LiftY = 3.05f;         // local y of a lifted (selected) ball center
        [Tooltip("Free world units above the lifted ball (set by GameMgr per row); flight arcs may rise this high.")]
        public float ArcHeadroom = 2f;

        public int Index { get; private set; }
        public int Capacity { get; private set; }
        public IReadOnlyList<Ball> Balls => _balls;
        public int Count => _balls.Count;
        public bool IsEmpty => _balls.Count == 0;
        public bool IsFull => _balls.Count >= Capacity;
        public bool IsSelected { get; private set; }
        public Ball TopBall => _balls.Count == 0 ? null : _balls[_balls.Count - 1];

        /// <summary>Number of same-colored balls at the top.</summary>
        public int TopBallSameCount
        {
            get
            {
                if (_balls.Count == 0) return 0;
                int c = TopBall.ColorIndex, n = 1;
                for (int i = _balls.Count - 2; i >= 0 && _balls[i].ColorIndex == c; i--) n++;
                return n;
            }
        }

        public bool IsUniform
        {
            get
            {
                if (_balls.Count <= 1) return true;
                int c = _balls[0].ColorIndex;
                for (int i = 1; i < _balls.Count; i++) if (_balls[i].ColorIndex != c) return false;
                return true;
            }
        }

        /// <summary>Full and one color.</summary>
        public bool IsCompleted => IsFull && IsUniform;

        /// <summary>Reference IsIdentical: empty counts as identical.</summary>
        public bool IsIdentical => IsEmpty || IsCompleted;

        /// <summary>True while any ball of this tube is animating (as import target or source) or the tube shakes.</summary>
        public bool IsMoving => _animating > 0 || _shake != null;

        readonly List<Ball> _balls = new List<Ball>();
        int _animating;
        Coroutine _shake;
        SpriteRenderer _sr;
        BoxCollider2D _col;

        public void Setup(int index, int capacity, Sprite tubeSprite, int sortingOrder)
        {
            Index = index;
            Capacity = capacity;
            _sr = GetComponent<SpriteRenderer>();
            _col = GetComponent<BoxCollider2D>();
            _sr.sprite = tubeSprite;
            _sr.sortingOrder = sortingOrder;
            // click area: the tube plus the space above it where lifted balls sit
            _col.size = new Vector2(TubeWidth + 0.3f, TubeHeight + 1.6f);
            _col.offset = new Vector2(0f, 0.8f);
            name = $"Tube_{index}";
        }

        public Vector3 SlotLocalPos(int slot) => new Vector3(0f, FirstBallY + slot * BallPitch, 0f);
        public Vector3 SlotWorldPos(int slot) => transform.TransformPoint(SlotLocalPos(slot));
        public Vector3 LiftWorldPos(int stackIndex = 0) => transform.TransformPoint(new Vector3(0f, LiftY + stackIndex * BallPitch, 0f));

        public void SetBalls(IList<int> colorsBottomFirst, Func<int, Ball> ballFactory)
        {
            ClearBalls();
            for (int i = 0; i < colorsBottomFirst.Count; i++)
            {
                var b = ballFactory(colorsBottomFirst[i]);
                b.transform.SetParent(transform, false);
                b.SnapTo(SlotWorldPos(i));
                _balls.Add(b);
            }
        }

        public void ClearBalls()
        {
            foreach (var b in _balls) if (b != null) Destroy(b.gameObject);
            _balls.Clear();
            IsSelected = false;
        }

        /// <summary>Re-seat every ball in its slot (after a layout change).</summary>
        public void SnapBalls()
        {
            for (int i = 0; i < _balls.Count; i++) _balls[i].SnapTo(SlotWorldPos(i));
            IsSelected = false;
        }

        // ---------- selection ----------

        /// <summary>Lifts only the top ball above the tube mouth (reference BallData.Select); the rest of the run follows on import.</summary>
        public void OnSelect()
        {
            if (IsEmpty || IsSelected) return;
            IsSelected = true;
            TopBall.RiseTo(LiftWorldPos(0), 0.09f);
        }

        /// <summary>Drops the lifted balls back into place.</summary>
        public void CancelSelect()
        {
            if (!IsSelected) return;
            IsSelected = false;
            for (int i = 0; i < _balls.Count; i++) _balls[i].SnapTo(SlotWorldPos(i));
        }

        // ---------- rules ----------

        /// <summary>Reference TubeData.CanImport(source): empty target accepts anything; otherwise not full and same top color.</summary>
        public bool CanImport(Tube source)
        {
            if (source == null || source == this || source.IsEmpty) return false;
            if (IsEmpty) return true;
            if (IsFull) return false;
            return source.TopBall.ColorIndex == TopBall.ColorIndex;
        }

        /// <summary>
        /// Moves n = min(source.TopBallSameCount, free slots) balls from source into this tube with an arc animation,
        /// then reports (from, to, count). Mirrors TubeData.Import.
        /// </summary>
        public int Import(Tube source, float moveSpeed, float perBallDelay, Action<Tube, Tube, int> onMoveEnd)
        {
            int n = Mathf.Min(source.TopBallSameCount, Capacity - _balls.Count);
            if (n <= 0) return 0;

            source.IsSelected = false;
            var moving = new List<Ball>(n);
            for (int i = 0; i < n; i++)
            {
                var b = source._balls[source._balls.Count - 1];
                source._balls.RemoveAt(source._balls.Count - 1);
                moving.Add(b);
            }
            // any remaining lifted balls of the source drop back
            for (int i = 0; i < source._balls.Count; i++) source._balls[i].SnapTo(source.SlotWorldPos(i));

            int remaining = n;
            for (int i = 0; i < n; i++)
            {
                var b = moving[i];
                int slot = _balls.Count;
                _balls.Add(b);
                b.transform.SetParent(transform, true);
                _animating++;
                source._animating++;
                // the first ball is already lifted; the others rise out of the source tube first
                b.FlyTo(source.LiftWorldPos(0), LiftWorldPos(0), SlotWorldPos(slot), moveSpeed, i * perBallDelay, Mathf.Min(ArcHeadroom, source.ArcHeadroom), () =>
                {
                    _animating--;
                    source._animating--;
                    remaining--;
                    if (remaining == 0) onMoveEnd?.Invoke(source, this, n);
                });
            }
            return n;
        }

        /// <summary>Moves count balls from the top of this tube back to target (Undo), animated.</summary>
        public void MoveBackTo(Tube target, int count, float moveSpeed, float perBallDelay, Action onDone)
        {
            count = Mathf.Min(count, _balls.Count);
            IsSelected = false;
            if (count == 0) { onDone?.Invoke(); return; }
            int remaining = count;
            for (int i = 0; i < count; i++)
            {
                var b = _balls[_balls.Count - 1];
                _balls.RemoveAt(_balls.Count - 1);
                int slot = target._balls.Count;
                target._balls.Add(b);
                b.transform.SetParent(target.transform, true);
                _animating++; target._animating++;
                b.FlyTo(LiftWorldPos(0), target.LiftWorldPos(0), target.SlotWorldPos(slot), moveSpeed, i * perBallDelay, Mathf.Min(ArcHeadroom, target.ArcHeadroom), () =>
                {
                    _animating--; target._animating--; remaining--;
                    if (remaining == 0) onDone?.Invoke();
                });
            }
        }

        // ---------- feedback ----------

        /// <summary>Horizontal wobble used when a move is rejected.</summary>
        public void Shake(float amplitude = 0.12f, float duration = 0.32f)
        {
            if (_shake != null) { StopCoroutine(_shake); transform.localPosition = _shakeBase; }
            _shake = StartCoroutine(ShakeRoutine(amplitude, duration));
        }

        Vector3 _shakeBase;

        IEnumerator ShakeRoutine(float amplitude, float duration)
        {
            _shakeBase = transform.localPosition;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = t / duration;
                float x = Mathf.Sin(k * Mathf.PI * 6f) * amplitude * (1f - k);
                transform.localPosition = _shakeBase + new Vector3(x, 0f, 0f);
                yield return null;
            }
            transform.localPosition = _shakeBase;
            _shake = null;
        }

        /// <summary>Small scale punch played when the tube becomes full and uniform.</summary>
        public void PlayTubeFull()
        {
            StartCoroutine(PunchRoutine());
        }

        IEnumerator PunchRoutine()
        {
            Vector3 s = transform.localScale;
            float t = 0f, d = 0.35f;
            while (t < d)
            {
                t += Time.deltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(t / d) * Mathf.PI);
                transform.localScale = s * (1f + 0.08f * k);
                yield return null;
            }
            transform.localScale = s;
        }

        public List<int> ColorsBottomFirst()
        {
            var l = new List<int>(_balls.Count);
            foreach (var b in _balls) l.Add(b.ColorIndex);
            return l;
        }
    }
}
