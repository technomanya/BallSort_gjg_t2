using System;
using System.Collections;
using System.Collections.Generic;
using BallSortGame.Core;
using UnityEngine;

namespace BallSortGame.Gameplay
{
    /// <summary>A tube holding a stack of balls, bottom first.</summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D))]
    public class Tube : MonoBehaviour
    {
        [Header("Skin (set by GameManager)")]
        public float TubeWidth = 1.81f;
        public float TubeHeight = 4.8f;
        public float FirstBallY = -1.79f;   // local y of the bottom ball center
        public float BallPitch = 1.08f;     // distance between stacked ball centers
        public float LiftY = 3.05f;         // local y of a lifted (selected) ball center
        [Tooltip("Free space above the lifted ball for flight arcs.")]
        public float ArcHeadroom = 2f;

        public int Index { get; private set; }
        public int Capacity { get; private set; }
        public TubeKind Kind { get; private set; } = TubeKind.Normal;
        public bool IsInOnly => Kind == TubeKind.InOnly;
        public bool IsOutOnly => Kind == TubeKind.OutOnly;
        /// <summary>The booster's little tube: one ball, never a finished tube.</summary>
        public bool IsSpare => Kind == TubeKind.Spare;
        /// <summary>Covered: nothing goes in or out until a same-row neighbour is completed.</summary>
        public bool IsHiddenTube => Kind == TubeKind.Hidden;
        public IReadOnlyList<Ball> Balls => _balls;
        public int Count => _balls.Count;
        public bool IsEmpty => _balls.Count == 0;
        public bool IsFull => _balls.Count >= Capacity;
        public bool IsSelected { get; private set; }
        public Ball TopBall => _balls.Count == 0 ? null : _balls[_balls.Count - 1];

        /// <summary>
        /// Same-coloured balls at the top. A hidden or heavy ball ends the run, since neither leaves with the balls above.
        /// </summary>
        public int TopBallSameCount
        {
            get
            {
                if (_balls.Count == 0) return 0;
                if (TopBall.IsHeavy) return 1;   // a heavy ball moves alone
                int c = TopBall.ColorIndex, n = 1;
                for (int i = _balls.Count - 2; i >= 0 && _balls[i].ColorIndex == c && !_balls[i].IsHidden && !_balls[i].IsHeavy; i--) n++;
                return n;
            }
        }

        public bool IsTopHeavy => _balls.Count > 0 && TopBall.IsHeavy;

        /// <summary>Bit k = the k-th ball from the bottom is heavy.</summary>
        public int HeavyMask
        {
            get { int m = 0; for (int k = 0; k < _balls.Count && k < 31; k++) if (_balls[k].IsHeavy) m |= 1 << k; return m; }
        }

        /// <summary>Bit k = the k-th ball from the bottom is still hidden.</summary>
        public int HiddenBallMask
        {
            get { int m = 0; for (int k = 0; k < _balls.Count && k < 31; k++) if (_balls[k].IsHidden) m |= 1 << k; return m; }
        }

        public bool HasHiddenBall
        {
            get { foreach (var b in _balls) if (b.IsHidden) return true; return false; }
        }

        /// <summary>The top ball is always visible. Call after balls leave and when the level starts.</summary>
        public void RevealTop()
        {
            if (_balls.Count > 0) _balls[_balls.Count - 1].Reveal();
        }

        public void RevealAll()
        {
            foreach (var b in _balls) b.Reveal();
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

        /// <summary>Full and one colour. The spare tube is only a parking spot and never counts, nor does a covered tube.</summary>
        public bool IsCompleted => !IsSpare && !IsHiddenTube && IsFull && IsUniform;

        /// <summary>Empty or completed, so the board can be won.</summary>
        public bool IsIdentical => IsEmpty || IsCompleted;

        /// <summary>A ball is flying in or out, or the tube shakes.</summary>
        public bool IsMoving => _animating > 0 || _shake != null;

        readonly List<Ball> _balls = new List<Ball>();
        int _animating;
        Coroutine _shake;
        Coroutine _opening;
        SpriteRenderer _sr;
        BoxCollider2D _col;
        SpriteRenderer _badge;
        SpriteRenderer _cover;

        const float BadgeSize = 0.7f;       // world units
        const float BadgeGap = 0.12f;       // between the glass bottom and the badge
        const int CoverOrderAbove = 3;      // above the glass, the balls and their "?" marks
        const float OpenDuration = 0.4f;

        public void Setup(int index, int capacity, Sprite tubeSprite, int sortingOrder)
        {
            Index = index;
            Capacity = capacity;
            _sr = GetComponent<SpriteRenderer>();
            _col = GetComponent<BoxCollider2D>();
            _sr.sprite = tubeSprite;
            _sr.sortingOrder = sortingOrder;
            // 9-sliced so only the straight middle stretches to TubeHeight
            bool sliced = tubeSprite != null && tubeSprite.border != Vector4.zero;
            _sr.drawMode = sliced ? SpriteDrawMode.Sliced : SpriteDrawMode.Simple;
            if (sliced) _sr.size = new Vector2(tubeSprite.bounds.size.x, TubeHeight);
            // click area: the tube plus the space above it where lifted balls sit
            float bottom = -TubeHeight / 2f;
            float top = Mathf.Max(TubeHeight / 2f + 1.6f, LiftY + 0.95f);
            _col.size = new Vector2(TubeWidth + 0.3f, top - bottom);
            _col.offset = new Vector2(0f, (top + bottom) / 2f);
            name = $"Tube_{index}";
        }

        /// <summary>
        /// One-way tubes get an arrow under the glass (up = out-only, down = in-only). A hidden tube gets the cover.
        /// </summary>
        public void SetKind(TubeKind kind, Sprite arrow, Sprite cover = null)
        {
            Kind = kind;
            SetCover(kind == TubeKind.Hidden, cover);
            if (kind == TubeKind.Normal || kind == TubeKind.Spare || kind == TubeKind.Hidden)
            {
                if (_badge != null) _badge.gameObject.SetActive(false);
                return;
            }
            if (_badge == null)
            {
                var go = new GameObject("kind_badge", typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                _badge = go.GetComponent<SpriteRenderer>();
                _badge.sortingOrder = (_sr != null ? _sr.sortingOrder : 3) + 1;
            }
            _badge.gameObject.SetActive(true);
            _badge.sprite = arrow;
            _badge.color = Color.white;
            _badge.flipY = kind == TubeKind.InOnly;
            float h = arrow != null ? arrow.bounds.size.y : 1f;
            float scale = h > 0f ? BadgeSize / h : 1f;
            _badge.transform.localScale = new Vector3(scale, scale, 1f);
            _badge.transform.localPosition = new Vector3(0f, -TubeHeight / 2f - BadgeGap - BadgeSize / 2f, 0f);
        }

        void SetCover(bool on, Sprite cover)
        {
            if (!on)
            {
                if (_cover != null) _cover.gameObject.SetActive(false);
                return;
            }
            if (_cover == null)
            {
                var go = new GameObject("hidden_cover", typeof(SpriteRenderer));
                go.transform.SetParent(transform, false);
                _cover = go.GetComponent<SpriteRenderer>();
            }
            if (_sr == null) _sr = GetComponent<SpriteRenderer>();
            _cover.gameObject.SetActive(true);
            _cover.sortingOrder = _sr.sortingOrder + CoverOrderAbove;
            // no art: use the glass tinted green
            _cover.sprite = cover != null ? cover : _sr.sprite;
            _cover.color = cover != null ? Color.white : new Color(0.25f, 0.55f, 0.42f, 1f);
            // same height and centre as the glass
            var glass = _sr.sprite == null ? new Bounds(Vector3.zero, new Vector3(TubeWidth, TubeHeight, 0f))
                : _sr.drawMode == SpriteDrawMode.Sliced ? new Bounds(Vector3.zero, _sr.size)   // centre pivot
                : _sr.sprite.bounds;
            float h = _cover.sprite != null ? _cover.sprite.bounds.size.y : 1f;
            float scale = h > 0f ? glass.size.y / h : 1f;
            _cover.transform.localScale = new Vector3(scale, scale, 1f);
            Vector3 c = _cover.sprite != null ? _cover.sprite.bounds.center * scale : Vector3.zero;
            _cover.transform.localPosition = new Vector3(glass.center.x - c.x, glass.center.y - c.y, 0f);
        }

        /// <summary>Level editor: a see-through cover so the balls under it can still be painted.</summary>
        public void SetCoverAlpha(float alpha)
        {
            if (_cover == null) return;
            var c = _cover.color;
            c.a = alpha;
            _cover.color = c;
        }

        /// <summary>Turns a hidden tube into a normal one and fades the cover. False if it was not hidden.</summary>
        public bool Open(bool animate = true)
        {
            if (!IsHiddenTube) return false;
            Kind = TubeKind.Normal;
            RevealTop();
            if (_cover == null) return true;
            if (animate && isActiveAndEnabled) _opening = StartCoroutine(OpenRoutine());
            else _cover.gameObject.SetActive(false);
            return true;
        }

        /// <summary>Undo of the move that opened this tube must cover it again.</summary>
        public void Close(Sprite cover)
        {
            if (_opening != null) { StopCoroutine(_opening); _opening = null; }
            SetKind(TubeKind.Hidden, null, cover);   // SetCover resets the cover's scale and colour
        }

        IEnumerator OpenRoutine()
        {
            var tr = _cover.transform;
            Vector3 s = tr.localScale;
            Color c = _cover.color;
            float t = 0f;
            while (t < OpenDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / OpenDuration);
                tr.localScale = s * (1f + 0.15f * k);
                _cover.color = new Color(c.r, c.g, c.b, c.a * (1f - k * k));
                yield return null;
            }
            _cover.gameObject.SetActive(false);
            tr.localScale = s;
            _cover.color = c;
            _opening = null;
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

        /// <summary>Re-seats every ball after a layout change.</summary>
        public void SnapBalls()
        {
            for (int i = 0; i < _balls.Count; i++) _balls[i].SnapTo(SlotWorldPos(i));
            IsSelected = false;
        }

        // --- selection

        /// <summary>Lifts only the top ball. The rest of the run follows on import.</summary>
        public void OnSelect()
        {
            if (IsEmpty || IsSelected) return;
            IsSelected = true;
            TopBall.RiseTo(LiftWorldPos(0), 0.09f);
        }

        public void CancelSelect()
        {
            if (!IsSelected) return;
            IsSelected = false;
            for (int i = 0; i < _balls.Count; i++) _balls[i].SnapTo(SlotWorldPos(i));
        }

        // --- rules

        public bool CanExport => !IsEmpty && !IsInOnly && !IsHiddenTube;

        /// <summary>
        /// Empty accepts anything, otherwise not full and same top colour. Keep in sync with BoardState.CanImport.
        /// </summary>
        public bool CanImport(Tube source)
        {
            if (source == null || source == this || !source.CanExport) return false;
            if (IsOutOnly || IsHiddenTube) return false;
            if (source.IsTopHeavy) return IsEmpty;   // a heavy ball sinks to the bottom, so only an empty tube takes it
            if (IsEmpty) return true;
            if (IsFull) return false;
            return source.TopBall.ColorIndex == TopBall.ColorIndex;
        }

        /// <summary>
        /// Flies the source's top run (up to the free slots) into this tube and reports (from, to, count) when done.
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
            // the rest drop back, and the new top ball shows its colour
            for (int i = 0; i < source._balls.Count; i++) source._balls[i].SnapTo(source.SlotWorldPos(i));
            source.RevealTop();

            int remaining = n;
            for (int i = 0; i < n; i++)
            {
                var b = moving[i];
                int slot = _balls.Count;
                _balls.Add(b);
                b.transform.SetParent(transform, true);
                _animating++;
                source._animating++;
                // the first ball is already lifted, the others rise out of the source first
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

        /// <summary>Undo: flies count balls from the top of this tube back to target.</summary>
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
            RevealTop();   // what is uncovered by the undo stays known
        }

        // --- feedback

        /// <summary>Wobble for a rejected move.</summary>
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

        /// <summary>Scale punch when the tube is completed.</summary>
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
