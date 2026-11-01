using System;
using System.Collections;
using System.Collections.Generic;
using BallSortGame.Core;
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
        [Header("Skin (set by GameManager)")]
        public float TubeWidth = 1.81f;
        public float TubeHeight = 4.8f;
        public float FirstBallY = -1.79f;   // local y of the bottom ball center
        public float BallPitch = 1.08f;     // distance between stacked ball centers
        public float LiftY = 3.05f;         // local y of a lifted (selected) ball center
        [Tooltip("Free world units above the lifted ball (set by GameManager per row); flight arcs may rise this high.")]
        public float ArcHeadroom = 2f;

        public int Index { get; private set; }
        public int Capacity { get; private set; }
        /// <summary>Normal, InOnly (balls go in, never out), OutOnly (balls come out, never in) or Hidden (covered until it opens).</summary>
        public TubeKind Kind { get; private set; } = TubeKind.Normal;
        public bool IsInOnly => Kind == TubeKind.InOnly;
        public bool IsOutOnly => Kind == TubeKind.OutOnly;
        /// <summary>The booster's little tube: one ball, never a finished tube.</summary>
        public bool IsSpare => Kind == TubeKind.Spare;
        /// <summary>Still covered: no ball goes in or out until a neighbouring tube is completed (see <see cref="Open"/>).</summary>
        public bool IsHiddenTube => Kind == TubeKind.Hidden;
        public IReadOnlyList<Ball> Balls => _balls;
        public int Count => _balls.Count;
        public bool IsEmpty => _balls.Count == 0;
        public bool IsFull => _balls.Count >= Capacity;
        public bool IsSelected { get; private set; }
        public Ball TopBall => _balls.Count == 0 ? null : _balls[_balls.Count - 1];

        /// <summary>
        /// Number of same-colored balls at the top. A hidden ball ends the run (its colour is not known yet), and so
        /// does a heavy ball under the run (it never leaves with the balls above it); a heavy top ball is a run of one.
        /// </summary>
        public int TopBallSameCount
        {
            get
            {
                if (_balls.Count == 0) return 0;
                int c = TopBall.ColorIndex, n = 1;
                for (int i = _balls.Count - 2; i >= 0 && _balls[i].ColorIndex == c && !_balls[i].IsHidden && !_balls[i].IsHeavy; i--) n++;
                return n;
            }
        }

        /// <summary>The top ball is heavy: it moves alone, and only into an empty tube.</summary>
        public bool IsTopHeavy => _balls.Count > 0 && TopBall.IsHeavy;

        /// <summary>Heavy balls as a bit mask, bit k = the k-th ball from the bottom (see <see cref="BoardState.HeavyMask"/>).</summary>
        public int HeavyMask
        {
            get { int m = 0; for (int k = 0; k < _balls.Count && k < 31; k++) if (_balls[k].IsHeavy) m |= 1 << k; return m; }
        }

        /// <summary>Any ball whose colour is still hidden.</summary>
        public bool HasHiddenBall
        {
            get { foreach (var b in _balls) if (b.IsHidden) return true; return false; }
        }

        /// <summary>The top ball is always visible: call after balls left the tube (and when the level starts).</summary>
        public void RevealTop()
        {
            if (_balls.Count > 0) _balls[_balls.Count - 1].Reveal();
        }

        /// <summary>Shows every ball (a completed tube has nothing left to hide).</summary>
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

        /// <summary>Full and one color. The little (spare) tube is never completed: it is only a parking spot. Nor is a covered tube.</summary>
        public bool IsCompleted => !IsSpare && !IsHiddenTube && IsFull && IsUniform;

        /// <summary>Reference IsIdentical: empty counts as identical.</summary>
        public bool IsIdentical => IsEmpty || IsCompleted;

        /// <summary>True while any ball of this tube is animating (as import target or source) or the tube shakes.</summary>
        public bool IsMoving => _animating > 0 || _shake != null;

        readonly List<Ball> _balls = new List<Ball>();
        int _animating;
        Coroutine _shake;
        SpriteRenderer _sr;
        BoxCollider2D _col;
        SpriteRenderer _badge;
        SpriteRenderer _cover;

        const float BadgeSize = 0.7f;       // world units; the arrow sprite is 1 unit at 100 ppu
        const float BadgeGap = 0.12f;       // space between the glass bottom and the badge
        const int CoverOrderAbove = 3;      // the cover sorts above the glass, the balls and their "?" marks
        const float OpenDuration = 0.4f;

        public void Setup(int index, int capacity, Sprite tubeSprite, int sortingOrder)
        {
            Index = index;
            Capacity = capacity;
            _sr = GetComponent<SpriteRenderer>();
            _col = GetComponent<BoxCollider2D>();
            _sr.sprite = tubeSprite;
            _sr.sortingOrder = sortingOrder;
            // click area: the tube plus the space above it where lifted balls sit
            float bottom = -TubeHeight / 2f;
            float top = Mathf.Max(TubeHeight / 2f + 1.6f, LiftY + 0.95f);
            _col.size = new Vector2(TubeWidth + 0.3f, top - bottom);
            _col.offset = new Vector2(0f, (top + bottom) / 2f);
            name = $"Tube_{index}";
        }

        /// <summary>
        /// Sets the one-way rule and shows it with an arrow under the glass: pointing up for out-only
        /// (balls leave), pointing down for in-only (balls enter). <paramref name="arrow"/> points up.
        /// A hidden tube wears <paramref name="cover"/> (Art/Tubes/Hidden tube) over the glass instead.
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
            // without the art: the glass itself, tinted leaf green
            _cover.sprite = cover != null ? cover : _sr.sprite;
            _cover.color = cover != null ? Color.white : new Color(0.25f, 0.55f, 0.42f, 1f);
            // same height and centre as the glass
            var glass = _sr.sprite != null ? _sr.sprite.bounds : new Bounds(Vector3.zero, new Vector3(TubeWidth, TubeHeight, 0f));
            float h = _cover.sprite != null ? _cover.sprite.bounds.size.y : 1f;
            float scale = h > 0f ? glass.size.y / h : 1f;
            _cover.transform.localScale = new Vector3(scale, scale, 1f);
            Vector3 c = _cover.sprite != null ? _cover.sprite.bounds.center * scale : Vector3.zero;
            _cover.transform.localPosition = new Vector3(glass.center.x - c.x, glass.center.y - c.y, 0f);
        }

        /// <summary>Level editor preview: a see-through cover, so the designer can still paint the balls under it.</summary>
        public void SetCoverAlpha(float alpha)
        {
            if (_cover == null) return;
            var c = _cover.color;
            c.a = alpha;
            _cover.color = c;
        }

        /// <summary>
        /// Opens a hidden tube for good (a neighbouring tube was completed): it plays as a normal tube from now on and
        /// the cover fades away. Returns false when the tube was not hidden.
        /// </summary>
        public bool Open(bool animate = true)
        {
            if (!IsHiddenTube) return false;
            Kind = TubeKind.Normal;
            RevealTop();
            if (_cover == null) return true;
            if (animate && isActiveAndEnabled) StartCoroutine(OpenRoutine());
            else _cover.gameObject.SetActive(false);
            return true;
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

        /// <summary>True when balls may be taken out of this tube at all (not empty, not in-only, not hidden).</summary>
        public bool CanExport => !IsEmpty && !IsInOnly && !IsHiddenTube;

        /// <summary>
        /// Reference TubeData.CanImport(source): empty target accepts anything; otherwise not full and same top color.
        /// One-way tubes: nothing leaves an in-only source, nothing enters an out-only target. A hidden tube does neither.
        /// Heavy balls: a heavy ball on top of its tube only goes into an empty tube (and moves alone).
        /// Mirrors <see cref="BoardState.CanImport"/>.
        /// </summary>
        public bool CanImport(Tube source)
        {
            if (source == null || source == this || !source.CanExport) return false;
            if (IsOutOnly || IsHiddenTube) return false;
            if (source.IsTopHeavy) return IsEmpty;
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
            // any remaining lifted balls of the source drop back; the new top ball shows its colour
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
            RevealTop();   // what is uncovered by the undo stays known
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
