using System;
using System.Collections.Generic;

namespace BallSortGame.Core
{
    /// <summary>Special tube rules. Stored per tube in levels ("tube_kinds"), designs and boards.</summary>
    public enum TubeKind : byte
    {
        Normal = 0,
        /// <summary>Balls can be put in but never taken out (starts empty).</summary>
        InOnly = 1,
        /// <summary>Balls can be taken out but never put in (starts filled).</summary>
        OutOnly = 2,
        /// <summary>
        /// Little tube from the in-game booster: a parking spot for one ball of any colour. It never counts as a
        /// finished tube, so a level is only won once it is empty again. Not used in level files.
        /// </summary>
        Spare = 3,
        /// <summary>
        /// Covered tube (Art/Tubes/Hidden tube): its balls cannot be seen, taken or added to until a neighbouring tube
        /// (index - 1 or + 1) is completed; then it opens for good and plays as a Normal tube. It may start empty or with balls.
        /// </summary>
        Hidden = 4,
    }

    /// <summary>
    /// Pure rule model of a ball-sort board (no Unity objects).
    /// Mirrors BallSort.Solver.BoardState from the reference game:
    /// tubes are byte stacks stored bottom-first, 0 = no ball, colors are 1..N.
    /// </summary>
    public sealed class BoardState
    {
        public const int MaxTubes = 24;
        public const int MaxDepth = 8;

        readonly byte[][] _colors;   // [tube][slot] bottom-first
        readonly byte[] _counts;
        readonly TubeKind[] _kinds;
        // Heavy balls (the cannonball obstacle): one bit per slot (bottom-first) of a normal coloured ball that is heavy.
        // A heavy ball only ever moves alone into an empty tube (so it lands at the bottom), and a run moving off a tube
        // stops at it. The level maker may put the weight on any ball; the rule only bites when the player moves it.
        readonly uint[] _heavy;
        public readonly int Capacity;

        /// <summary>
        /// Tubes per row on the board (GameManager.MaxTubesPerRow); decides which tubes stand side by side, see
        /// <see cref="TubeRows"/>. A hidden tube opens only from a tube physically next to it.
        /// </summary>
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
            _colors = new byte[tubeCount][];
            for (int i = 0; i < tubeCount; i++) _colors[i] = new byte[capacity];
        }

        public TubeKind KindOf(int tube) => _kinds[tube];

        /// <summary>The ball in <paramref name="slot"/> (0 = bottom) is heavy; false for an empty slot.</summary>
        public bool IsHeavyAt(int tube, int slot) => slot >= 0 && slot < _counts[tube] && ((_heavy[tube] >> slot) & 1u) != 0;

        /// <summary>The top ball of the tube is heavy: it moves alone, and only into an empty tube.</summary>
        public bool IsTopHeavy(int tube) => _counts[tube] > 0 && IsHeavyAt(tube, _counts[tube] - 1);

        /// <summary>Heavy balls of the tube as a bit mask, bit s = slot s (bottom-first). The level JSON form.</summary>
        public int HeavyMask(int tube) => (int)_heavy[tube];

        /// <summary>Marks one ball heavy (or normal again). Ignored for an empty slot.</summary>
        public void SetHeavy(int tube, int slot, bool heavy)
        {
            if (slot < 0 || slot >= _counts[tube]) return;
            if (heavy) _heavy[tube] |= 1u << slot;
            else _heavy[tube] &= ~(1u << slot);
        }

        /// <summary>Sets the tube's heavy balls from a bit mask (bit s = slot s, bottom-first); bits above the balls are dropped.</summary>
        public void SetHeavyMask(int tube, int mask) => _heavy[tube] = mask <= 0 ? 0u : (uint)mask & BallMask(tube);

        uint BallMask(int tube) => _counts[tube] >= 32 ? uint.MaxValue : (1u << _counts[tube]) - 1u;

        /// <summary>Applies per-tube heavy masks (null or short = no heavy ball in the rest). 1 = the bottom ball, the old form.</summary>
        public void SetHeavies(IList<int> heavy)
        {
            for (int i = 0; i < TubeCount; i++) SetHeavyMask(i, heavy != null && i < heavy.Count ? heavy[i] : 0);
        }

        public bool HasHeavyBalls
        {
            get { for (int t = 0; t < TubeCount; t++) if (_heavy[t] != 0) return true; return false; }
        }

        /// <summary>Heavy masks per tube (bit s = slot s, bottom-first), or null when no ball is heavy (the compact form for level JSON).</summary>
        public List<int> HeavyOrNull()
        {
            if (!HasHeavyBalls) return null;
            var l = new List<int>(TubeCount);
            for (int t = 0; t < TubeCount; t++) l.Add(HeavyMask(t));
            return l;
        }

        /// <summary>Balls the tube holds: <see cref="Capacity"/>, or one for a <see cref="TubeKind.Spare"/> tube.</summary>
        public int CapacityOf(int tube) => _kinds[tube] == TubeKind.Spare ? Math.Min(1, Capacity) : Capacity;
        public void SetKind(int tube, TubeKind kind) => _kinds[tube] = kind;

        /// <summary>Applies per-tube kinds (null or short = Normal for the rest).</summary>
        public void SetKinds(IList<int> kinds)
        {
            for (int i = 0; i < TubeCount; i++) _kinds[i] = kinds != null && i < kinds.Count ? (TubeKind)kinds[i] : TubeKind.Normal;
        }

        public bool HasSpecialTubes
        {
            get { foreach (var k in _kinds) if (k != TubeKind.Normal) return true; return false; }
        }

        /// <summary>Kinds as ints, or null when every tube is Normal (the compact form for level JSON).</summary>
        public List<int> KindsOrNull()
        {
            if (!HasSpecialTubes) return null;
            var l = new List<int>(TubeCount);
            foreach (var k in _kinds) l.Add((int)k);
            return l;
        }

        /// <summary>Empty and Normal: only such tubes are interchangeable for the solvers.</summary>
        public bool IsPlainEmpty(int tube) => _counts[tube] == 0 && _kinds[tube] == TubeKind.Normal;

        /// <summary>True when a tube physically next to <paramref name="tube"/> (left or right, same row) is hidden.</summary>
        public bool HasHiddenNeighbour(int tube)
        {
            int l = TubeRows.Left(tube, TubeCount, MaxTubesPerRow), r = TubeRows.Right(tube, TubeCount, MaxTubesPerRow);
            return (l >= 0 && _kinds[l] == TubeKind.Hidden) || (r >= 0 && _kinds[r] == TubeKind.Hidden);
        }

        /// <summary>True when a tube physically next to <paramref name="tube"/> (left or right, same row) is completed.</summary>
        public bool HasCompletedNeighbour(int tube)
        {
            int l = TubeRows.Left(tube, TubeCount, MaxTubesPerRow), r = TubeRows.Right(tube, TubeCount, MaxTubesPerRow);
            return (l >= 0 && IsCompleted(l)) || (r >= 0 && IsCompleted(r));
        }

        /// <summary>
        /// Opens every <see cref="TubeKind.Hidden"/> tube physically next to a completed tube (left or right in the same
        /// row, see <see cref="TubeRows"/>; it becomes Normal). Chains: an opened tube that is already completed opens its
        /// other neighbour too. Returns true when a tube opened.
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

        /// <summary>Builds a board from level data where each tube is listed TOP-first (0 = empty slot).</summary>
        public static BoardState FromTopFirst(IList<IList<int>> tubesTopFirst, IList<int> kinds = null, IList<int> heavy = null)
        {
            int capacity = 0;
            foreach (var t in tubesTopFirst) capacity = Math.Max(capacity, t.Count);
            var board = new BoardState(tubesTopFirst.Count, capacity);
            for (int i = 0; i < tubesTopFirst.Count; i++)
            {
                var tube = tubesTopFirst[i];
                // iterate from the bottom (last index) upwards
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
                Buffer.BlockCopy(_colors[i], 0, b._colors[i], 0, Capacity);
            }
            return b;
        }

        public int Count(int tube) => _counts[tube];
        public bool IsEmpty(int tube) => _counts[tube] == 0;
        public bool IsFull(int tube) => _counts[tube] >= CapacityOf(tube);
        public int FreeSlots(int tube) => CapacityOf(tube) - _counts[tube];

        /// <summary>Color of slot i (0 = bottom). 0 when empty.</summary>
        public int ColorAt(int tube, int slot) => slot < _counts[tube] ? _colors[tube][slot] : 0;

        public int TopColor(int tube) => _counts[tube] == 0 ? 0 : _colors[tube][_counts[tube] - 1];

        /// <summary>
        /// Length of the same-colored run at the top of the tube. A heavy ball on top is a run of one (it moves alone);
        /// a heavy ball under the run ends it (it never leaves with the balls above it).
        /// </summary>
        public int TopRun(int tube)
        {
            int n = _counts[tube];
            if (n == 0) return 0;
            if (IsHeavyAt(tube, n - 1)) return 1;
            byte top = _colors[tube][n - 1];
            int run = 1;
            for (int i = n - 2; i >= 0 && _colors[tube][i] == top && !IsHeavyAt(tube, i); i--) run++;
            return run;
        }

        /// <summary>True when every ball in the tube has the same color (empty counts as uniform).</summary>
        public bool IsUniform(int tube)
        {
            int n = _counts[tube];
            if (n <= 1) return true;
            byte c = _colors[tube][0];
            for (int i = 1; i < n; i++) if (_colors[tube][i] != c) return false;
            return true;
        }

        /// <summary>Reference rule IsTubeIdentical: empty, or full and one color. A spare tube only when empty.</summary>
        public bool IsTubeIdentical(int tube)
        {
            if (_counts[tube] == 0) return true;
            return IsCompleted(tube);
        }

        /// <summary>Full and one color (a completed tube). A spare tube is never completed, nor is a tube still hidden.</summary>
        public bool IsCompleted(int tube) => _kinds[tube] != TubeKind.Spare && _kinds[tube] != TubeKind.Hidden && _counts[tube] >= Capacity && IsUniform(tube);

        public bool IsWin()
        {
            for (int i = 0; i < TubeCount; i++) if (!IsTubeIdentical(i)) return false;
            return true;
        }

        public int CompletedCount()
        {
            int c = 0;
            for (int i = 0; i < TubeCount; i++) if (IsCompleted(i)) c++;
            return c;
        }

        /// <summary>Can the top of <paramref name="from"/> be exported at all (reference CanExport + completed check).</summary>
        public bool CanExport(int from, bool allowSelectFullTube = false)
        {
            if (_counts[from] == 0) return false;
            if (_kinds[from] == TubeKind.InOnly || _kinds[from] == TubeKind.Hidden) return false;
            if (!allowSelectFullTube && IsCompleted(from)) return false;
            return true;
        }

        /// <summary>
        /// Reference rule BoardState.CanImport(from, to). A heavy ball on top of its tube goes into an empty tube only,
        /// so once moved it sits at the bottom.
        /// </summary>
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

        /// <summary>min(run length of the top color in from, free slots in to). The game always moves the whole run.</summary>
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
            // a completed tube opens the hidden tubes physically next to it
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
            _heavy[tube] &= ~(1u << _counts[tube]);   // the weight leaves with its ball (ApplyMove puts it on again)
            return c;
        }

        /// <summary>Appends an empty tube (reference AddTube).</summary>
        public BoardState WithExtraTube()
        {
            var b = new BoardState(TubeCount + 1, Capacity) { MaxTubesPerRow = MaxTubesPerRow };
            for (int i = 0; i < TubeCount; i++)
            {
                b._counts[i] = _counts[i];
                b._kinds[i] = _kinds[i];
                b._heavy[i] = _heavy[i];
                Buffer.BlockCopy(_colors[i], 0, b._colors[i], 0, Capacity);
            }
            return b;
        }

        /// <summary>Any legal move at all (quick dead-end check, independent of solvability).</summary>
        public bool HasAnyMove()
        {
            for (int f = 0; f < TubeCount; f++)
            {
                if (!CanExport(f)) continue;
                for (int t = 0; t < TubeCount; t++)
                    if (CanImport(f, t)) return true;
            }
            return false;
        }

        /// <summary>
        /// Any move left that changes something (the reference's HasNoMove rule): a legal move that is not pouring a
        /// single-colour tube into an empty tube of the same or greater height, which only relocates the balls. The game
        /// fails a level only when this is false, however hopeless the board already is.
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
        /// Any useful move (<see cref="HasUsefulMove"/>) that reaches a board not in <paramref name="seen"/> (hashes of
        /// the boards played so far, <see cref="ComputeHash"/>). False when every move left only pours balls back and
        /// forth between positions the player has already been in.
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
        /// FNV-1a hash over the tube contents. Tubes are hashed in a canonical (sorted) order so that
        /// boards that only differ by tube permutation collapse to one state.
        /// </summary>
        public ulong ComputeHash()
        {
            Span<ulong> keys = stackalloc ulong[MaxTubes];
            for (int i = 0; i < TubeCount; i++)
            {
                ulong k = 1469598103934665603UL ^ ((ulong)_kinds[i] << 56);   // kind is part of the tube identity
                k ^= (ulong)_heavy[i] << 40;                                      // and so are its heavy balls
                for (int s = 0; s < Capacity; s++)
                {
                    k ^= _colors[i][s];
                    k *= 1099511628211UL;
                }
                keys[i] = k;
            }
            // insertion sort (TubeCount <= 24)
            for (int i = 1; i < TubeCount; i++)
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
