using System;
using System.Collections.Generic;

namespace BallSortGame.Core
{
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
        public readonly int Capacity;

        public int TubeCount => _counts.Length;

        public BoardState(int tubeCount, int capacity)
        {
            if (tubeCount <= 0 || tubeCount > MaxTubes) throw new ArgumentOutOfRangeException(nameof(tubeCount));
            if (capacity <= 0 || capacity > MaxDepth) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            _counts = new byte[tubeCount];
            _colors = new byte[tubeCount][];
            for (int i = 0; i < tubeCount; i++) _colors[i] = new byte[capacity];
        }

        /// <summary>Builds a board from level data where each tube is listed TOP-first (0 = empty slot).</summary>
        public static BoardState FromTopFirst(IList<IList<int>> tubesTopFirst)
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
            return board;
        }

        public BoardState Clone()
        {
            var b = new BoardState(TubeCount, Capacity);
            for (int i = 0; i < TubeCount; i++)
            {
                b._counts[i] = _counts[i];
                Buffer.BlockCopy(_colors[i], 0, b._colors[i], 0, Capacity);
            }
            return b;
        }

        public int Count(int tube) => _counts[tube];
        public bool IsEmpty(int tube) => _counts[tube] == 0;
        public bool IsFull(int tube) => _counts[tube] >= Capacity;
        public int FreeSlots(int tube) => Capacity - _counts[tube];

        /// <summary>Color of slot i (0 = bottom). 0 when empty.</summary>
        public int ColorAt(int tube, int slot) => slot < _counts[tube] ? _colors[tube][slot] : 0;

        public int TopColor(int tube) => _counts[tube] == 0 ? 0 : _colors[tube][_counts[tube] - 1];

        /// <summary>Length of the same-colored run at the top of the tube.</summary>
        public int TopRun(int tube)
        {
            int n = _counts[tube];
            if (n == 0) return 0;
            byte top = _colors[tube][n - 1];
            int run = 1;
            for (int i = n - 2; i >= 0 && _colors[tube][i] == top; i--) run++;
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

        /// <summary>Reference rule IsTubeIdentical: empty, or full and one color.</summary>
        public bool IsTubeIdentical(int tube)
        {
            if (_counts[tube] == 0) return true;
            return _counts[tube] >= Capacity && IsUniform(tube);
        }

        /// <summary>Full and one color (a completed tube).</summary>
        public bool IsCompleted(int tube) => _counts[tube] >= Capacity && IsUniform(tube);

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
            if (!allowSelectFullTube && IsCompleted(from)) return false;
            return true;
        }

        /// <summary>Reference rule BoardState.CanImport(from, to).</summary>
        public bool CanImport(int from, int to, bool allowSelectFullTube = false)
        {
            if (from == to) return false;
            if (!CanExport(from, allowSelectFullTube)) return false;
            if (_counts[to] == 0) return true;
            if (_counts[to] >= Capacity) return false;
            return TopColor(to) == TopColor(from);
        }

        /// <summary>min(run length of the top color in from, free slots in to). The game always moves the whole run.</summary>
        public int CountTopSameMovable(int from, int to) => Math.Min(TopRun(from), FreeSlots(to));

        public void ApplyMove(int from, int to, int n)
        {
            for (int i = 0; i < n; i++)
            {
                byte c = Pop(from);
                Push(to, c);
            }
        }

        public void Push(int tube, byte color)
        {
            if (_counts[tube] >= Capacity) throw new InvalidOperationException("tube full");
            _colors[tube][_counts[tube]++] = color;
        }

        public byte Pop(int tube)
        {
            if (_counts[tube] == 0) throw new InvalidOperationException("tube empty");
            byte c = _colors[tube][--_counts[tube]];
            _colors[tube][_counts[tube]] = 0;
            return c;
        }

        /// <summary>Appends an empty tube (reference AddTube).</summary>
        public BoardState WithExtraTube()
        {
            var b = new BoardState(TubeCount + 1, Capacity);
            for (int i = 0; i < TubeCount; i++)
            {
                b._counts[i] = _counts[i];
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
        /// FNV-1a hash over the tube contents. Tubes are hashed in a canonical (sorted) order so that
        /// boards that only differ by tube permutation collapse to one state.
        /// </summary>
        public ulong ComputeHash()
        {
            Span<ulong> keys = stackalloc ulong[MaxTubes];
            for (int i = 0; i < TubeCount; i++)
            {
                ulong k = 1469598103934665603UL;
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
                sb.Append('[');
                for (int s = 0; s < Capacity; s++) { if (s > 0) sb.Append(','); sb.Append(_colors[i][s]); }
                sb.Append("] ");
            }
            return sb.ToString();
        }
    }
}
