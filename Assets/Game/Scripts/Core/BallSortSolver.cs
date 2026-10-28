using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BallSortGame.Core
{
    public enum SolveVerdict { Solvable, Unsolvable, Unknown }

    public struct SolverBudget
    {
        public int MaxStates;
        public int MaxMilliseconds;
        public static SolverBudget Default => new SolverBudget { MaxStates = 200_000, MaxMilliseconds = 400 };
    }

    public struct SolverMove
    {
        public int From, To, Count;
        public SolverMove(int from, int to, int count) { From = from; To = to; Count = count; }
    }

    /// <summary>
    /// Iterative depth-first solver, modelled on BallSort.Solver.BallSortSolver of the reference game:
    /// visited-hash pruning, candidate scoring, skipping the immediate reverse move and "pure relocations"
    /// (moving an entire uniform tube into an empty tube), and a state/time budget.
    /// Used for dead-end ("no moves") detection and hints.
    /// </summary>
    public sealed class BallSortSolver
    {
        sealed class Frame
        {
            public BoardState State;
            public List<ScoredMove> Candidates;
            public int Next;
            public SolverMove Parent;
            public bool HasParent;
        }

        struct ScoredMove
        {
            public SolverMove Move;
            public int Score;
        }

        readonly HashSet<ulong> _visited = new HashSet<ulong>();
        readonly Stack<Frame> _stack = new Stack<Frame>();

        public int StatesVisited { get; private set; }
        public List<SolverMove> Solution { get; private set; }

        public SolveVerdict Solve(BoardState start, SolverBudget budget, bool allowBreakCompletedTube = false)
        {
            _visited.Clear();
            _stack.Clear();
            Solution = null;
            StatesVisited = 0;

            if (start.IsWin()) { Solution = new List<SolverMove>(); return SolveVerdict.Solvable; }

            var sw = Stopwatch.StartNew();
            var root = new Frame { State = start.Clone(), HasParent = false };
            root.Candidates = GenerateCandidates(root, allowBreakCompletedTube);
            _visited.Add(root.State.ComputeHash());
            _stack.Push(root);

            while (_stack.Count > 0)
            {
                if (StatesVisited >= budget.MaxStates || sw.ElapsedMilliseconds >= budget.MaxMilliseconds)
                    return SolveVerdict.Unknown;

                var frame = _stack.Peek();
                if (frame.Next >= frame.Candidates.Count)
                {
                    _stack.Pop();
                    continue;
                }

                var cand = frame.Candidates[frame.Next++];
                var next = frame.State.Clone();
                next.ApplyMove(cand.Move.From, cand.Move.To, cand.Move.Count);
                StatesVisited++;

                if (next.IsWin())
                {
                    Solution = new List<SolverMove>();
                    foreach (var f in _stack) { } // stack enumerates top-first; rebuild below
                    var frames = _stack.ToArray(); // top first
                    for (int i = frames.Length - 1; i >= 0; i--)
                    {
                        var fr = frames[i];
                        if (fr.HasParent) Solution.Add(fr.Parent);
                    }
                    Solution.Add(cand.Move);
                    return SolveVerdict.Solvable;
                }

                ulong h = next.ComputeHash();
                if (!_visited.Add(h)) continue;

                var child = new Frame { State = next, Parent = cand.Move, HasParent = true };
                child.Candidates = GenerateCandidates(child, allowBreakCompletedTube);
                _stack.Push(child);
            }
            return SolveVerdict.Unsolvable;
        }

        List<ScoredMove> GenerateCandidates(Frame frame, bool allowBreakCompletedTube)
        {
            var s = frame.State;
            var list = new List<ScoredMove>();
            int n = s.TubeCount;
            for (int from = 0; from < n; from++)
            {
                // source tube eligible = not empty, not completed (unless allowed)
                if (!s.CanExport(from, allowBreakCompletedTube)) continue;
                int fromRun = s.TopRun(from);
                bool fromUniform = s.IsUniform(from);
                int topColor = s.TopColor(from);
                bool seenEmptyTarget = false;
                for (int to = 0; to < n; to++)
                {
                    if (to == from) continue;
                    if (!s.CanImport(from, to, allowBreakCompletedTube)) continue;
                    int count = s.CountTopSameMovable(from, to);
                    if (count <= 0) continue;

                    bool toEmpty = s.IsEmpty(to);
                    // pure relocation: whole uniform tube -> empty tube changes nothing
                    if (toEmpty && fromUniform && count == s.Count(from)) continue;
                    // all empty tubes are equivalent: only try the first one
                    if (toEmpty)
                    {
                        if (seenEmptyTarget) continue;
                        seenEmptyTarget = true;
                    }
                    // immediate reverse of the parent move
                    if (frame.HasParent && frame.Parent.From == to && frame.Parent.To == from && frame.Parent.Count == count) continue;

                    int score;
                    if (!toEmpty && s.TopColor(to) == topColor)
                        score = (s.Count(to) + count >= s.Capacity ? 150 : 50) + count;
                    else if (toEmpty)
                        score = count + 10;
                    else
                        score = count;
                    list.Add(new ScoredMove { Move = new SolverMove(from, to, count), Score = score });
                }
            }
            list.Sort((a, b) => b.Score.CompareTo(a.Score));
            return list;
        }
    }
}
