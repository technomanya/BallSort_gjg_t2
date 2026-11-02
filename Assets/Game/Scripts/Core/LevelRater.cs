using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BallSortGame.Core
{
    /// <summary>What the rater measured for one board.</summary>
    public sealed class LevelRating
    {
        public SolveVerdict Verdict;
        /// <summary>Shortest solution found. Optimal only when <see cref="AStarOptimal"/>.</summary>
        public int OptimalMoves;
        public bool AStarOptimal;
        public int AStarExpanded;
        /// <summary>Solution length and search effort of the greedy solver.</summary>
        public int GreedyMoves;
        public int GreedyStates;
        public int LowerBound;
        /// <summary>Share of random playouts that solved the level, and their mean length.</summary>
        public double PlayoutWinRate;
        public double PlayoutMeanMoves;
        public int Playouts;
        public int Colors;
        public int Tubes;
        public int Empties;
        /// <summary>Playout loss rate, 0..1.</summary>
        public double Score;
        /// <summary>Difficulty bucket 1..7.</summary>
        public int Bucket;
        public List<SolverMove> Solution;
        public long Milliseconds;

        public override string ToString() =>
            $"{Verdict} bucket {Bucket} score {Score:0.00} opt {OptimalMoves}{(AStarOptimal ? "" : "+")} lb {LowerBound} greedy {GreedyMoves}/{GreedyStates} ({Milliseconds} ms)";
    }

    public struct RaterBudget
    {
        public int AStarMaxNodes;
        public int AStarMaxMilliseconds;
        public int GreedyMaxStates;
        public int GreedyMaxMilliseconds;
        /// <summary>Random playouts (0 = skip).</summary>
        public int Playouts;
        /// <summary>Buckets 6 and 7 need ~200+ playouts to tell 0.975 from 0.995.</summary>
        public static RaterBudget Default => new RaterBudget { AStarMaxNodes = 300_000, AStarMaxMilliseconds = 1200, GreedyMaxStates = 200_000, GreedyMaxMilliseconds = 600, Playouts = 400 };
        /// <summary>No A*: the bucket does not depend on it, and this is ~10x faster on big boards.</summary>
        public static RaterBudget Fast => new RaterBudget { AStarMaxNodes = 0, AStarMaxMilliseconds = 0, GreedyMaxStates = 100_000, GreedyMaxMilliseconds = 250, Playouts = 200 };
    }

    /// <summary>
    /// Difficulty rater. The greedy and A* solvers prove the level solvable and find a short solution for "steps",
    /// then a random-playout bot plays it and its loss rate maps to buckets 1..7. Calibration lives in
    /// <see cref="Score"/> and <see cref="BucketUpperBounds"/>.
    /// </summary>
    public sealed class LevelRater
    {
        readonly BallSortSolver _greedy = new BallSortSolver();
        readonly AStarSolver _astar = new AStarSolver();

        public LevelRating Rate(BoardState board, RaterBudget budget)
        {
            var sw = Stopwatch.StartNew();
            var r = new LevelRating
            {
                Colors = CountColors(board),
                Tubes = board.TubeCount,
                Empties = CountEmpties(board),
                LowerBound = AStarSolver.Heuristic(board),
            };

            var gv = _greedy.Solve(board, new SolverBudget { MaxStates = budget.GreedyMaxStates, MaxMilliseconds = budget.GreedyMaxMilliseconds });
            r.GreedyStates = _greedy.StatesVisited;
            r.GreedyMoves = gv == SolveVerdict.Solvable ? _greedy.Solution.Count : -1;
            var greedySolution = gv == SolveVerdict.Solvable ? _greedy.Solution : null;

            // optimal A* first, then weighted A* (at most 2x optimal) if that runs out of budget.
            // Skipped when the budget has no A* and greedy found a path: the bucket only needs the playouts.
            var av = SolveVerdict.Unknown;
            bool optimal = false;
            if (budget.AStarMaxNodes > 0 || greedySolution == null)
            {
                // floor, so a failed greedy run still gets a real A* try
                int nodes = Math.Max(budget.AStarMaxNodes, 20_000);
                int ms = Math.Max(budget.AStarMaxMilliseconds, 200);
                av = _astar.Solve(board, nodes, ms, 1.0);
                r.AStarExpanded = _astar.Expanded;
                optimal = av == SolveVerdict.Solvable;
                if (av == SolveVerdict.Unknown)
                {
                    av = _astar.Solve(board, nodes, ms / 2, 2.0);
                    r.AStarExpanded += _astar.Expanded;
                }
            }
            if (av == SolveVerdict.Solvable)
            {
                r.OptimalMoves = _astar.Solution.Count;
                r.AStarOptimal = optimal;
                r.Solution = _astar.Solution;
                r.Verdict = SolveVerdict.Solvable;
                if (greedySolution != null && greedySolution.Count < r.OptimalMoves)
                {
                    r.OptimalMoves = greedySolution.Count;
                    r.Solution = greedySolution;
                }
            }
            else if (greedySolution != null)
            {
                r.OptimalMoves = greedySolution.Count;
                r.AStarOptimal = false;
                r.Solution = greedySolution;
                r.Verdict = SolveVerdict.Solvable;
            }
            else
            {
                r.Verdict = av == SolveVerdict.Unsolvable || gv == SolveVerdict.Unsolvable ? SolveVerdict.Unsolvable : SolveVerdict.Unknown;
                r.OptimalMoves = -1;
            }

            if (r.Verdict == SolveVerdict.Solvable)
            {
                if (budget.Playouts > 0)
                {
                    r.Playouts = budget.Playouts;
                    PlayoutBot.Run(board, budget.Playouts, 12345, out r.PlayoutWinRate, out r.PlayoutMeanMoves);
                }
                r.Score = Score(r);
                r.Bucket = BucketFor(r.Score);
            }
            r.Milliseconds = sw.ElapsedMilliseconds;
            return r;
        }

        /// <summary>
        /// Share of random playouts that end stuck (0..1). On 595 seeds with known bot buckets it correlates +0.87
        /// with the bucket, while solution length, search effort and board size do not separate buckets.
        /// Mean loss rate per bucket 1..7 was 0.22 / 0.54 / 0.67 / 0.76 / 0.93 / 0.98 / 1.00.
        /// </summary>
        public static double Score(LevelRating r) => r.Playouts > 0 ? 1.0 - r.PlayoutWinRate : 0.0;

        /// <summary>
        /// Upper loss-rate bound of buckets 1..6, bucket 7 is above. Fitted to match the bot bucket exactly for
        /// 76 % of the calibration seeds and within one for 92 %.
        /// </summary>
        public static readonly double[] BucketUpperBounds = { 0.50, 0.60, 0.70, 0.87, 0.975, 0.995 };

        public static int BucketFor(double score)
        {
            for (int i = 0; i < BucketUpperBounds.Length; i++)
                if (score < BucketUpperBounds[i]) return i + 1;
            return BucketUpperBounds.Length + 1;
        }

        static int CountColors(BoardState b)
        {
            var seen = new HashSet<int>();
            for (int t = 0; t < b.TubeCount; t++)
                for (int s = 0; s < b.Count(t); s++) seen.Add(b.ColorAt(t, s));
            return seen.Count;
        }

        static int CountEmpties(BoardState b)
        {
            int n = 0;
            for (int t = 0; t < b.TubeCount; t++) if (b.IsEmpty(t)) n++;
            return n;
        }
    }

    /// <summary>A* over canonical board states. A move transfers the whole top run (as many balls as fit), like the game.</summary>
    public sealed class AStarSolver
    {
        sealed class Node
        {
            public BoardState State;
            public Node Parent;
            public SolverMove Move;
            public int G;
            public int F;
        }

        public int Expanded { get; private set; }
        public List<SolverMove> Solution { get; private set; }

        /// <summary>
        /// Admissible lower bound: the larger of (colour segments - colours) and the sum over colours of
        /// (tubes holding it - 1). A move removes at most one segment and clears a colour from at most one tube.
        /// </summary>
        public static int Heuristic(BoardState b)
        {
            int segments = 0;
            Span<int> tubesPerColor = stackalloc int[256];
            Span<int> lastTube = stackalloc int[256];
            for (int c = 0; c < 256; c++) lastTube[c] = -1;
            int colors = 0;
            for (int t = 0; t < b.TubeCount; t++)
            {
                int n = b.Count(t);
                int prev = -1;
                for (int s = 0; s < n; s++)
                {
                    int c = b.ColorAt(t, s);
                    if (c != prev) { segments++; prev = c; }
                    if (lastTube[c] != t)
                    {
                        lastTube[c] = t;
                        if (tubesPerColor[c]++ == 0) colors++;
                    }
                }
            }
            int spread = 0;
            for (int c = 1; c < 256; c++) if (tubesPerColor[c] > 1) spread += tubesPerColor[c] - 1;
            return Math.Max(Math.Max(0, segments - colors), spread);
        }

        /// <summary><paramref name="weight"/> 1 = optimal A*, above 1 = weighted A* (faster, at most weight x optimal).</summary>
        public SolveVerdict Solve(BoardState start, int maxNodes, int maxMilliseconds, double weight = 1.0)
        {
            Expanded = 0;
            Solution = null;
            if (start.IsWin()) { Solution = new List<SolverMove>(); return SolveVerdict.Solvable; }

            var sw = Stopwatch.StartNew();
            var open = new MinHeap<Node>();
            var best = new Dictionary<ulong, int>();
            long tie = 0;
            var root = new Node { State = start.Clone(), G = 0 };
            root.F = (int)Math.Round(weight * Heuristic(root.State));
            open.Push(root, ((long)root.F << 40) | (tie++ & 0xFFFFFFFFFF));
            best[root.State.ComputeHash()] = 0;

            while (open.Count > 0)
            {
                if (Expanded >= maxNodes || sw.ElapsedMilliseconds >= maxMilliseconds) return SolveVerdict.Unknown;
                var node = open.Pop();
                var s = node.State;
                if (s.IsWin())
                {
                    var path = new List<SolverMove>();
                    for (var p = node; p.Parent != null; p = p.Parent) path.Add(p.Move);
                    path.Reverse();
                    Solution = path;
                    return SolveVerdict.Solvable;
                }
                ulong hs = s.ComputeHash();
                if (best.TryGetValue(hs, out int bg) && bg < node.G) continue; // stale entry
                Expanded++;

                int n = s.TubeCount;
                for (int from = 0; from < n; from++)
                {
                    if (!s.CanExport(from)) continue;
                    bool fromUniform = s.IsUniform(from);
                    bool seenEmpty = false;
                    for (int to = 0; to < n; to++)
                    {
                        if (to == from || !s.CanImport(from, to)) continue;
                        int count = s.CountTopSameMovable(from, to);
                        if (count <= 0) continue;
                        bool toEmpty = s.IsEmpty(to);
                        if (toEmpty && s.IsPlainEmpty(to) && s.KindOf(from) == TubeKind.Normal)
                        {
                            if (fromUniform && count == s.Count(from)) continue; // pure relocation
                            if (seenEmpty) continue;                            // all plain empty tubes are equivalent
                            seenEmpty = true;
                        }
                        var next = s.Clone();
                        next.ApplyMove(from, to, count);
                        ulong h = next.ComputeHash();
                        int g = node.G + 1;
                        if (best.TryGetValue(h, out int old) && old <= g) continue;
                        best[h] = g;
                        var child = new Node { State = next, Parent = node, Move = new SolverMove(from, to, count), G = g };
                        child.F = g + (int)Math.Round(weight * Heuristic(next));
                        open.Push(child, ((long)child.F << 40) | (tie++ & 0xFFFFFFFFFF));
                    }
                }
            }
            return SolveVerdict.Unsolvable;
        }
    }

    /// <summary>
    /// Casual player model: random legal moves, preferring moves that stack a colour on itself, never undoing the
    /// last move. Gives up when stuck or at a move cap. Its win share says how easily a player gets stuck.
    /// </summary>
    public static class PlayoutBot
    {
        public static void Run(BoardState start, int playouts, int rngSeed, out double winRate, out double meanMoves)
        {
            var rng = new Random(rngSeed);
            int wins = 0; long winMoves = 0;
            int lb = AStarSolver.Heuristic(start);
            int cap = lb * 4 + 40;   // move cap, part of the calibration
            var moves = new List<(int from, int to, int count, bool good)>(64);
            for (int p = 0; p < playouts; p++)
            {
                var s = start.Clone();
                int lastFrom = -1, lastTo = -1;
                for (int step = 0; step < cap; step++)
                {
                    if (s.IsWin()) { wins++; winMoves += step; break; }
                    moves.Clear();
                    int goodCount = 0;
                    int n = s.TubeCount;
                    for (int from = 0; from < n; from++)
                    {
                        if (!s.CanExport(from)) continue;
                        bool fromUniform = s.IsUniform(from);
                        int topColor = s.TopColor(from);
                        bool seenEmpty = false;
                        for (int to = 0; to < n; to++)
                        {
                            if (to == from || !s.CanImport(from, to)) continue;
                            int count = s.CountTopSameMovable(from, to);
                            if (count <= 0) continue;
                            bool toEmpty = s.IsEmpty(to);
                            if (toEmpty && s.IsPlainEmpty(to) && s.KindOf(from) == TubeKind.Normal)
                            {
                                if (fromUniform && count == s.Count(from)) continue;
                                if (seenEmpty) continue;
                                seenEmpty = true;
                            }
                            if (from == lastTo && to == lastFrom) continue;
                            bool good = !toEmpty && s.TopColor(to) == topColor;
                            if (good) goodCount++;
                            moves.Add((from, to, count, good));
                        }
                    }
                    if (moves.Count == 0) break;
                    int pick;
                    if (goodCount > 0 && rng.NextDouble() < 0.7)   // stacking bias, part of the calibration
                    {
                        int k = rng.Next(goodCount);
                        pick = 0;
                        for (int i = 0; i < moves.Count; i++) if (moves[i].good && k-- == 0) { pick = i; break; }
                    }
                    else pick = rng.Next(moves.Count);
                    var m = moves[pick];
                    s.ApplyMove(m.from, m.to, m.count);
                    lastFrom = m.from; lastTo = m.to;
                }
            }
            winRate = playouts > 0 ? wins / (double)playouts : 0;
            meanMoves = wins > 0 ? winMoves / (double)wins : 0;
        }
    }

    /// <summary>Binary min-heap keyed by a long. Unity's .NET profile has no PriorityQueue.</summary>
    sealed class MinHeap<T>
    {
        T[] _items = new T[256];
        long[] _keys = new long[256];
        public int Count { get; private set; }

        public void Push(T item, long key)
        {
            if (Count == _items.Length)
            {
                Array.Resize(ref _items, Count * 2);
                Array.Resize(ref _keys, Count * 2);
            }
            int i = Count++;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_keys[p] <= key) break;
                _items[i] = _items[p]; _keys[i] = _keys[p];
                i = p;
            }
            _items[i] = item; _keys[i] = key;
        }

        public T Pop()
        {
            var top = _items[0];
            Count--;
            if (Count > 0)
            {
                var item = _items[Count]; long key = _keys[Count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1;
                    if (l >= Count) break;
                    int r = l + 1;
                    int c = r < Count && _keys[r] < _keys[l] ? r : l;
                    if (_keys[c] >= key) break;
                    _items[i] = _items[c]; _keys[i] = _keys[c];
                    i = c;
                }
                _items[i] = item; _keys[i] = key;
            }
            _items[Count] = default;
            return top;
        }
    }
}
