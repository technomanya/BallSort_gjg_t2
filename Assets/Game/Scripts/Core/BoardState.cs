using System;
using System.Collections.Generic;

namespace BallSortGame.Core
{
    /// <summary>Per-tube rule. Saved in level JSON as "tube_kinds".</summary>
    public enum TubeKind : byte
    {
        Normal = 0,
        /// <summary>Balls go in but never come out. Starts empty.</summary>
        InOnly = 1,
        /// <summary>Balls come out but never go in. Starts filled.</summary>
        OutOnly = 2,
        /// <summary>
        /// The booster's little tube: holds one ball of any colour and never counts as finished, so the level is
        /// won only once it is empty. Runtime only, never in level files.
        /// </summary>
        Spare = 3,
        /// <summary>
        /// Covered tube: its balls cannot be seen, taken or added to until a tube beside it in the same row is
        /// completed. Then it opens for good as a Normal tube.
        /// </summary>
        Hidden = 4,
    }

    /// <summary>
    /// Pure rule model of a board, no Unity objects. Tubes are byte stacks stored bottom-first,
    /// 0 = no ball, colours 1..N.
    /// </summary>
    public sealed class BoardState
    {
        public const int MaxTubes = 24;
        public const int MaxDepth = 8;

        readonly byte[][] _colors;   // [tube][slot] bottom-first
        readonly byte[] _counts;
        readonly TubeKind[] _kinds;
        // Heavy balls, bit s = slot s. A heavy ball moves alone and only into an empty tube, and a run moving
        // off a tube stops at it. Any ball may be heavy, the rule only matters when it moves.
        readonly uint[] _heavy;
        // Hidden ("?") balls, bit s = slot s. A run stops at one because the player can't see that it matches.
        // Only the game's dead-end check sets these, the generator and rater play with full sight.
        readonly uint[] _unseen;
        public readonly int Capacity;

        /// <summary>Must match the game's row width: a hidden tube opens only from a tube beside it in the same row.</summary>
        public int MaxTubesPerRow = TubeRows.DefaultMaxPerRow;

        public int TubeCount => _counts.Length;

        public BoardState(int tubeCount, int capacity)
        {
            if (tubeCount <= 0 || tubeCount > MaxTubes) throw new ArgumentOutOfRangeException(nameof(tubeCount));
            if (capacity <= 0 || capacity > MaxDepth) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            _counts = new byte[tubeCount];
            _kinds = new TubeKind[tubeCount];
            _heavy = new uint[tubeCount];
            _unseen = new uint[tubeCount];
            _colors = new byte[tubeCount][];
            for (int i = 0; i < tubeCount; i++) _colors[i] = new byte[capacity];
        }

        public TubeKind KindOf(int tube) => _kinds[tube];

        /// <summary>Slot 0 is the bottom. False for an empty slot.</summary>
        public bool IsHeavyAt(int tube, int slot) => slot >= 0 && slot < _counts[tube] && ((_heavy[tube] >> slot) & 1u) != 0;

        public bool IsTopHeavy(int tube) => _counts[tube] > 0 && IsHeavyAt(tube, _counts[tube] - 1);

        /// <summary>Heavy balls as a bit mask, bit s = slot s. Same form as the level JSON.</summary>
        public int HeavyMask(int tube) => (int)_heavy[tube];

        /// <summary>Bits above the tube's balls are dropped.</summary>
        public void SetHeavyMask(int tube, int mask) => _heavy[tube] = mask <= 0 ? 0u : (uint)mask & BallMask(tube);

        /// <summary>Marks hidden ("?") balls, bit s = slot s. The top ball is always visible, so its bit is dropped.</summary>
        public void SetUnseenMask(int tube, int mask)
        {
            uint m = mask <= 0 ? 0u : (uint)mask & BallMask(tube);
            if (_counts[tube] > 0) m &= ~(1u << (_counts[tube] - 1));
            _unseen[tube] = m;
        }

        bool IsUnseenAt(int tube, int slot) => ((_unseen[tube] >> slot) & 1u) != 0;

        uint BallMask(int tube) => _counts[tube] >= 32 ? uint.MaxValue : (1u << _counts[tube]) - 1u;

        /// <summary>Per-tube heavy masks. Null or a short list = no heavy balls in the rest.</summary>
        public void SetHeavies(IList<int> heavy)
        {
            for (int i = 0; i < TubeCount; i++) SetHeavyMask(i, heavy != null && i < heavy.Count ? heavy[i] : 0);
        }

        public bool HasHeavyBalls
        {
            get { for (int t = 0; t < TubeCount; t++) if (_heavy[t] != 0) return true; return false; }
        }

        /// <summary>Heavy masks per tube, or null when no ball is heavy (compact level JSON).</summary>
        public List<int> HeavyOrNull()
        {
            if (!HasHeavyBalls) return null;
            var l = new List<int>(TubeCount);
            for (int t = 0; t < TubeCount; t++) l.Add(HeavyMask(t));
            return l;
        }

        /// <summary>A Spare tube holds one ball.</summary>
        public int CapacityOf(int tube) => _kinds[tube] == TubeKind.Spare ? Math.Min(1, Capacity) : Capacity;
        public void SetKind(int tube, TubeKind kind) => _kinds[tube] = kind;

        /// <summary>Null or a short list = Normal for the rest.</summary>
        public void SetKinds(IList<int> kinds)
        {
            for (int i = 0; i < TubeCount; i++) _kinds[i] = kinds != null && i < kinds.Count ? (TubeKind)kinds[i] : TubeKind.Normal;
        }

        public bool HasSpecialTubes
        {
            get { foreach (var k in _kinds) if (k != TubeKind.Normal) return true; return false; }
        }

        /// <summary>Kinds as ints, or null when every tube is Normal (compact level JSON).</summary>
        public List<int> KindsOrNull()
        {
            if (!HasSpecialTubes) return null;
            var l = new List<int>(TubeCount);
            foreach (var k in _kinds) l.Add((int)k);
            return l;
        }

        /// <summary>
        /// Empty, Normal and not beside a hidden tube. Only such tubes are interchangeable for the solvers, since
        /// completing a tube beside a hidden one opens it.
        /// </summary>
        public bool IsPlainEmpty(int tube) => _counts[tube] == 0 && _kinds[tube] == TubeKind.Normal && !HasHiddenNeighbour(tube);

        /// <summary>Left or right in the same row only.</summary>
        public bool HasHiddenNeighbour(int tube)
        {
            int l = TubeRows.Left(tube, TubeCount, MaxTubesPerRow), r = TubeRows.Right(tube, TubeCount, MaxTubesPerRow);
            return (l >= 0 && _kinds[l] == TubeKind.Hidden) || (r >= 0 && _kinds[r] == TubeKind.Hidden);
        }

        /// <summary>Left or right in the same row only.</summary>
        public bool HasCompletedNeighbour(int tube)
        {
            int l = TubeRows.Left(tube, TubeCount, MaxTubesPerRow), r = TubeRows.Right(tube, TubeCount, MaxTubesPerRow);
            return (l >= 0 && IsCompleted(l)) || (r >= 0 && IsCompleted(r));
        }

        /// <summary>
        /// Turns every hidden tube beside a completed tube into a Normal one. Repeats, because an opened tube
        /// may already be completed and open the next. True when any tube opened.
        /// </summary>
        public bool OpenHiddenTubes()
        {
            bool any = false, changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < TubeCount; i++)
                {
                    if (_kinds[i] != TubeKind.Hidden) continue;
                    if (HasCompletedNeighbour(i))
                    {
                        _kinds[i] = TubeKind.Normal;
                        any = changed = true;
                    }
                }
            }
            return any;
        }

        public bool HasHiddenTubes
        {
            get { foreach (var k in _kinds) if (k == TubeKind.Hidden) return true; return false; }
        }

        /// <summary>Builds a board from level data, each tube listed TOP-first (0 = empty slot).</summary>
        public static BoardState FromTopFirst(IList<IList<int>> tubesTopFirst, IList<int> kinds = null, IList<int> heavy = null)
        {
            int capacity = 0;
            foreach (var t in tubesTopFirst) capacity = Math.Max(capacity, t.Count);
            var board = new BoardState(tubesTopFirst.Count, capacity);
            for (int i = 0; i < tubesTopFirst.Count; i++)
            {
                var tube = tubesTopFirst[i];
                // bottom (last index) first
                for (int j = tube.Count - 1; j >= 0; j--)
                {
                    int c = tube[j];
                    if (c <= 0) continue;
                    board.Push(i, (byte)c);
                }
            }
            board.SetKinds(kinds);
            board.SetHeavies(heavy);
            board.OpenHiddenTubes();
            return board;
        }

        public BoardState Clone()
        {
            var b = new BoardState(TubeCount, Capacity) { MaxTubesPerRow = MaxTubesPerRow };
            for (int i = 0; i < TubeCount; i++)
            {
                b._counts[i] = _counts[i];
                b._kinds[i] = _kinds[i];
                b._heavy[i] = _heavy[i];
                b._unseen[i] = _unseen[i];
                Buffer.BlockCopy(_colors[i], 0, b._colors[i], 0, Capacity);
            }
            return b;
        }

        public int Count(int tube) => _counts[tube];
        public bool IsEmpty(int tube) => _counts[tube] == 0;
        public int FreeSlots(int tube) => CapacityOf(tube) - _counts[tube];

        /// <summary>Slot 0 is the bottom. 0 when empty.</summary>
        public int ColorAt(int tube, int slot) => slot < _counts[tube] ? _colors[tube][slot] : 0;

        public int TopColor(int tube) => _counts[tube] == 0 ? 0 : _colors[tube][_counts[tube] - 1];

        /// <summary>
        /// Length of the same-colour run on top. A heavy ball on top moves alone, and a heavy or hidden ball
        /// below ends the run.
        /// </summary>
        public int TopRun(int tube)
        {
            int n = _counts[tube];
            if (n == 0) return 0;
            if (IsHeavyAt(tube, n - 1)) return 1;
            byte top = _colors[tube][n - 1];
            int run = 1;
            for (int i = n - 2; i >= 0 && _colors[tube][i] == top && !IsHeavyAt(tube, i) && !IsUnseenAt(tube, i); i--) run++;
            return run;
        }

        /// <summary>Every ball the same colour. Empty counts.</summary>
        public bool IsUniform(int tube)
        {
            int n = _counts[tube];
            if (n <= 1) return true;
            byte c = _colors[tube][0];
            for (int i = 1; i < n; i++) if (_colors[tube][i] != c) return false;
            return true;
        }

        /// <summary>Empty or completed. A spare tube only when empty.</summary>
        public bool IsTubeIdentical(int tube)
        {
            if (_counts[tube] == 0) return true;
            return IsCompleted(tube);
        }

        /// <summary>Full and one colour. A spare or still-hidden tube never counts.</summary>
        public bool IsCompleted(int tube) => _kinds[tube] != TubeKind.Spare && _kinds[tube] != TubeKind.Hidden && _counts[tube] >= Capacity && IsUniform(tube);

        public bool IsWin()
        {
            for (int i = 0; i < TubeCount; i++) if (!IsTubeIdentical(i)) return false;
            return true;
        }

        /// <summary>Can the top of <paramref name="from"/> be picked up at all.</summary>
        public bool CanExport(int from, bool allowSelectFullTube = false)
        {
            if (_counts[from] == 0) return false;
            if (_kinds[from] == TubeKind.InOnly || _kinds[from] == TubeKind.Hidden) return false;
            if (!allowSelectFullTube && IsCompleted(from)) return false;
            return true;
        }

        /// <summary>A heavy ball goes only into an empty tube, so it always lands at the bottom.</summary>
        public bool CanImport(int from, int to, bool allowSelectFullTube = false)
        {
            if (from == to) return false;
            if (!CanExport(from, allowSelectFullTube)) return false;
            if (_kinds[to] == TubeKind.OutOnly || _kinds[to] == TubeKind.Hidden) return false;
            if (IsTopHeavy(from)) return _counts[to] == 0;
            if (_counts[to] == 0) return true;
            if (_counts[to] >= CapacityOf(to)) return false;
            return TopColor(to) == TopColor(from);
        }

        /// <summary>Top run capped by the free slots in <paramref name="to"/>. The game always moves the whole run.</summary>
        public int CountTopSameMovable(int from, int to) => Math.Min(TopRun(from), FreeSlots(to));

        public void ApplyMove(int from, int to, int n)
        {
            for (int i = 0; i < n; i++)
            {
                bool heavy = IsHeavyAt(from, _counts[from] - 1);   // the weight travels with its ball
                byte c = Pop(from);
                Push(to, c);
                if (heavy) _heavy[to] |= 1u << (_counts[to] - 1);
            }
            if (_counts[from] > 0) _unseen[from] &= ~(1u << (_counts[from] - 1));   // the new top ball shows its colour
            // completing a tube opens the hidden tubes beside it
            if (IsCompleted(to) && HasHiddenNeighbour(to))
                OpenHiddenTubes();
        }

        public void Push(int tube, byte color)
        {
            if (_counts[tube] >= CapacityOf(tube)) throw new InvalidOperationException("tube full");
            _colors[tube][_counts[tube]++] = color;
        }

        public byte Pop(int tube)
        {
            if (_counts[tube] == 0) throw new InvalidOperationException("tube empty");
            byte c = _colors[tube][--_counts[tube]];
            _colors[tube][_counts[tube]] = 0;
            _unseen[tube] &= ~(1u << _counts[tube]);
            _heavy[tube] &= ~(1u << _counts[tube]);   // the weight leaves with its ball (ApplyMove puts it back)
            return c;
        }

        /// <summary>Copy with one empty tube added at the end (the Add Tube booster).</summary>
        public BoardState WithExtraTube()
        {
            var b = new BoardState(TubeCount + 1, Capacity) { MaxTubesPerRow = MaxTubesPerRow };
            for (int i = 0; i < TubeCount; i++)
            {
                b._counts[i] = _counts[i];
                b._kinds[i] = _kinds[i];
                b._heavy[i] = _heavy[i];
                b._unseen[i] = _unseen[i];
                Buffer.BlockCopy(_colors[i], 0, b._colors[i], 0, Capacity);
            }
            return b;
        }

        /// <summary>
        /// Any legal move that changes something. Pouring a one-colour tube into an empty tube at least as tall only
        /// relocates it. The game fails a level only when this is false.
        /// </summary>
        public bool HasUsefulMove()
        {
            for (int f = 0; f < TubeCount; f++)
            {
                if (!CanExport(f)) continue;
                bool uniform = IsUniform(f);
                for (int t = 0; t < TubeCount; t++)
                    if (IsUsefulMove(f, t, uniform)) return true;
            }
            return false;
        }

        /// <summary>
        /// Any useful move that reaches a board not in <paramref name="seen"/> (hashes of the boards played so far).
        /// False when every move left only goes back to an earlier position.
        /// </summary>
        public bool HasNewMove(HashSet<ulong> seen)
        {
            for (int f = 0; f < TubeCount; f++)
            {
                if (!CanExport(f)) continue;
                bool uniform = IsUniform(f);
                for (int t = 0; t < TubeCount; t++)
                {
                    if (!IsUsefulMove(f, t, uniform)) continue;
                    var next = Clone();
                    next.ApplyMove(f, t, CountTopSameMovable(f, t));
                    if (!seen.Contains(next.ComputeHash())) return true;
                }
            }
            return false;
        }

        bool IsUsefulMove(int from, int to, bool fromUniform)
        {
            if (!CanImport(from, to)) return false;
            return !(fromUniform && _counts[to] == 0 && CapacityOf(to) >= CapacityOf(from));   // pointless relocation
        }

        /// <summary>
        /// FNV-1a hash of the board. Tubes are hashed in sorted order, so boards that differ only in tube order
        /// count as one state.
        /// </summary>
        public ulong ComputeHash()
        {
            Span<ulong> keys = stackalloc ulong[MaxTubes];
            for (int i = 0; i < TubeCount; i++)
            {
                ulong k = 1469598103934665603UL ^ ((ulong)_kinds[i] << 56);   // kind is part of the tube identity
                k ^= (ulong)_heavy[i] << 40;                                      // and so are its heavy balls
                k ^= (ulong)_unseen[i] << 20;                                     // and its hidden ("?") balls
                for (int s = 0; s < Capacity; s++)
                {
                    k ^= _colors[i][s];
                    k *= 1099511628211UL;
                }
                keys[i] = k;
            }
            // insertion sort (TubeCount <= 24), skipped while a tube is hidden: order then decides which tube opens it
            bool keepOrder = HasHiddenTubes;
            for (int i = 1; i < TubeCount && !keepOrder; i++)
            {
                ulong v = keys[i]; int j = i - 1;
                while (j >= 0 && keys[j] > v) { keys[j + 1] = keys[j]; j--; }
                keys[j + 1] = v;
            }
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < TubeCount; i++)
            {
                h ^= keys[i];
                h *= 1099511628211UL;
                h ^= h >> 29;
            }
            return h;
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < TubeCount; i++)
            {
                if (_kinds[i] == TubeKind.InOnly) sb.Append('v');
                else if (_kinds[i] == TubeKind.OutOnly) sb.Append('^');
                else if (_kinds[i] == TubeKind.Hidden) sb.Append('#');
                sb.Append('[');
                for (int s = 0; s < Capacity; s++)
                {
                    if (s > 0) sb.Append(',');
                    sb.Append(_colors[i][s]);
                    if (IsHeavyAt(i, s)) sb.Append('H');
                }
                sb.Append("] ");
            }
            return sb.ToString();
        }
    }
}
