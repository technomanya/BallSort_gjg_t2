using System;
using System.Collections.Generic;

namespace BallSortGame.Core
{
    public enum IssueSeverity { Error, Warning }

    /// <summary>One problem found in a level pack: which level slot and fill, and what is wrong.</summary>
    public sealed class LevelIssue
    {
        /// <summary>1-based level number in the pack.</summary>
        public int Level;
        /// <summary>0 = the main fill, 1.. = variants, 1000+ = dlb fills (the indices of <see cref="LevelData.ForAttempt"/>).</summary>
        public int SeedIndex;
        public string PuzzleId;
        public IssueSeverity Severity;
        public string Message;
        /// <summary>The fill the issue is about (null for pack-wide issues).</summary>
        public LevelData Data;

        /// <summary>Overrides the "Level N" place name, e.g. the name of the editor level the bot played alone.</summary>
        public string Label;

        public string SeedLabel => SeedIndex <= 0 ? "" : SeedIndex >= 1000 ? $" dlb {SeedIndex - 999}" : $" seed {SeedIndex + 1}";
        public string Where => Label != null ? Label + SeedLabel : Level > 0 ? $"Level {Level}{SeedLabel}" : "Pack";

        public override string ToString() =>
            $"[{(Severity == IssueSeverity.Error ? "ERROR" : "warn")}] {Where}{(string.IsNullOrEmpty(PuzzleId) ? "" : $" ({PuzzleId})")}: {Message}";
    }

    /// <summary>Everything the audit learned about one fill.</summary>
    public sealed class AuditResult
    {
        public readonly List<LevelIssue> Issues = new List<LevelIssue>();
        /// <summary>Solver result; null when the structure was too broken to build a board.</summary>
        public LevelRating Rating;
        /// <summary>Well-formed and proven solvable: the bot can play it.</summary>
        public bool Playable => Rating != null && Rating.Verdict == SolveVerdict.Solvable && Rating.Solution != null;
    }

    /// <summary>
    /// Pack sanity checks for the level bot and the editor tools: malformed fills (ball counts, gaps, sizes, one-way
    /// tube rules, heavy marks on missing balls), unsolvable fills (with a note when only the Add Tube booster saves them), fills the solver cannot
    /// decide, stored "steps" hints that do not solve the level, and duplicate puzzle ids. No Unity types: safe to
    /// run on a worker thread.
    /// </summary>
    public static class LevelAudit
    {
        /// <summary>Budget for the fill the bot plays: a short solution is wanted, so A* runs.</summary>
        public static RaterBudget PlayBudget => new RaterBudget { AStarMaxNodes = 150_000, AStarMaxMilliseconds = 700, GreedyMaxStates = 200_000, GreedyMaxMilliseconds = 600, Playouts = 100 };
        /// <summary>Budget for the other fills of a slot: solvability only.</summary>
        public static RaterBudget CheckBudget => new RaterBudget { AStarMaxNodes = 0, AStarMaxMilliseconds = 0, GreedyMaxStates = 200_000, GreedyMaxMilliseconds = 600, Playouts = 0 };

        /// <summary>Structural problem of a fill, or null when it is well-formed (every colour has exactly capacity balls, no gaps, legal sizes and tube kinds, not solved yet, some free space).</summary>
        public static string Structure(LevelData lv)
        {
            if (lv == null || lv.Tubes == null || lv.Tubes.Count == 0) return "no tubes";
            if (lv.Tubes.Count > BoardState.MaxTubes) return $"{lv.Tubes.Count} tubes; the game supports at most {BoardState.MaxTubes}";
            int cap = lv.Capacity;
            if (cap <= 0) return "the tubes have no slots";
            if (cap > BoardState.MaxDepth) return $"tube height {cap}; the game supports at most {BoardState.MaxDepth}";

            var perColor = new Dictionary<int, int>();
            int balls = 0;
            for (int i = 0; i < lv.Tubes.Count; i++)
            {
                var t = lv.Tubes[i];
                if (t == null || t.Count != cap) return $"tube {i + 1} has {t?.Count ?? 0} slots, the others {cap}";
                bool seenBall = false;
                for (int j = 0; j < t.Count; j++)   // top-first: once a ball is seen, everything below must be a ball
                {
                    int v = t[j];
                    if (v < 0) return $"tube {i + 1} has an unknown colour {v}";
                    if (v > 0) { seenBall = true; balls++; perColor[v] = perColor.TryGetValue(v, out int n) ? n + 1 : 1; }
                    else if (seenBall) return $"tube {i + 1} has a gap: an empty slot under a ball";
                }
            }
            if (balls == 0) return "no balls";
            var colors = new List<int>(perColor.Keys);
            colors.Sort();
            foreach (int c in colors)
                if (perColor[c] != cap) return $"colour {c} has {perColor[c]} balls, a colour needs exactly {cap}";

            if (lv.TubeKinds != null)
            {
                if (lv.TubeKinds.Count != lv.Tubes.Count) return $"tube_kinds lists {lv.TubeKinds.Count} tubes, the level has {lv.Tubes.Count}";
                for (int i = 0; i < lv.TubeKinds.Count; i++)
                {
                    int k = lv.TubeKinds[i];
                    if (k < 0 || k > 4 || k == (int)TubeKind.Spare) return $"tube {i + 1} has an unknown kind {k}";
                    if (k == (int)TubeKind.Hidden && TubeRows.Left(i, lv.Tubes.Count) < 0 && TubeRows.Right(i, lv.Tubes.Count) < 0)
                        return $"tube {i + 1} is hidden but has no tube next to it in its row to open it";
                    int count = Filled(lv.Tubes[i]);
                    if (k == (int)TubeKind.InOnly && count > 0) return $"tube {i + 1} is in-only but starts with balls";
                    if (k == (int)TubeKind.OutOnly && count == 0) return $"tube {i + 1} is out-only but starts empty";
                }
            }
            if (lv.Heavy != null)
            {
                if (lv.Heavy.Count != lv.Tubes.Count) return $"heavy lists {lv.Heavy.Count} tubes, the level has {lv.Tubes.Count}";
                for (int i = 0; i < lv.Heavy.Count; i++)
                    if (lv.Heavy[i] < 0 || (lv.Heavy[i] >> Filled(lv.Tubes[i])) != 0) return $"tube {i + 1} marks a heavy ball where it has no ball";
            }
            if (balls == lv.Tubes.Count * cap) return "every tube is full: no move is possible";
            var board = lv.ToBoard();
            if (board.IsWin()) return "already solved at the start";
            return null;
        }

        static int Filled(List<int> topFirst)
        {
            int n = 0;
            foreach (int v in topFirst) if (v > 0) n++;
            return n;
        }

        /// <summary>Replays the stored "steps" hint. Null when there are none or they solve the level, else the problem.</summary>
        public static string CheckSteps(LevelData lv)
        {
            if (lv.Steps == null || lv.Steps.Count == 0) return null;
            var b = lv.ToBoard();
            for (int i = 0; i < lv.Steps.Count; i++)
            {
                var s = lv.Steps[i];
                if (s == null || s.Count < 2) return $"stored step {i + 1} is malformed";
                int from = s[0], to = s[1];
                if (from < 0 || from >= b.TubeCount || to < 0 || to >= b.TubeCount) return $"stored step {i + 1} ({from + 1} -> {to + 1}) names a tube the level does not have";
                if (!b.CanImport(from, to)) return $"stored step {i + 1} ({from + 1} -> {to + 1}) is not a legal move";
                b.ApplyMove(from, to, b.CountTopSameMovable(from, to));
            }
            return b.IsWin() ? null : $"the {lv.Steps.Count} stored steps do not finish the level";
        }

        /// <summary>Full check of one fill: structure, solvability (with the Add Tube note), stored steps.</summary>
        public static AuditResult Audit(LevelData lv, int level, int seedIndex, RaterBudget budget)
        {
            var r = new AuditResult();
            void Add(IssueSeverity sev, string msg) => r.Issues.Add(new LevelIssue { Level = level, SeedIndex = seedIndex, PuzzleId = lv?.PuzzleId, Severity = sev, Message = msg, Data = lv });

            var structure = Structure(lv);
            if (structure != null) { Add(IssueSeverity.Error, structure); return r; }

            var board = lv.ToBoard();
            r.Rating = new LevelRater().Rate(board, budget);
            switch (r.Rating.Verdict)
            {
                case SolveVerdict.Unsolvable:
                    bool withTube = board.TubeCount < BoardState.MaxTubes
                                    && new BallSortSolver().Solve(board.WithExtraTube(), new SolverBudget { MaxStates = budget.GreedyMaxStates, MaxMilliseconds = budget.GreedyMaxMilliseconds }) == SolveVerdict.Solvable;
                    Add(IssueSeverity.Error, withTube ? "unsolvable without the Add Tube booster" : "unsolvable");
                    break;
                case SolveVerdict.Unknown:
                    Add(IssueSeverity.Warning, "the solver could not decide within its budget (very large board?)");
                    break;
            }
            var steps = CheckSteps(lv);
            if (steps != null) Add(IssueSeverity.Warning, steps);
            return r;
        }

        /// <summary>Checks every fill of a level slot except the main one (variants and dlb fills).</summary>
        public static List<LevelIssue> AuditOtherSeeds(LevelData slot, int level, RaterBudget budget)
        {
            var issues = new List<LevelIssue>();
            if (slot.Variants != null)
                for (int i = 0; i < slot.Variants.Count; i++) issues.AddRange(Audit(slot.Variants[i], level, i + 1, budget).Issues);
            if (slot.DlbVariants != null)
                for (int i = 0; i < slot.DlbVariants.Count; i++) issues.AddRange(Audit(slot.DlbVariants[i], level, 1000 + i, budget).Issues);
            return issues;
        }

        /// <summary>Pack-wide checks: duplicate puzzle ids across all fills.</summary>
        public static List<LevelIssue> AuditPack(StageData stage)
        {
            var issues = new List<LevelIssue>();
            var seen = new Dictionary<string, string>();
            void Visit(LevelData lv, int level, int seedIndex)
            {
                if (lv == null || string.IsNullOrEmpty(lv.PuzzleId)) return;
                string where = $"level {level}" + new LevelIssue { SeedIndex = seedIndex }.SeedLabel;
                if (seen.TryGetValue(lv.PuzzleId, out var first))
                    issues.Add(new LevelIssue { Level = level, SeedIndex = seedIndex, PuzzleId = lv.PuzzleId, Severity = IssueSeverity.Warning, Message = $"puzzle id also used by {first}", Data = lv });
                else seen[lv.PuzzleId] = where;
            }
            for (int i = 0; i < stage.Levels.Count; i++)
            {
                var slot = stage.Levels[i];
                Visit(slot, i + 1, 0);
                if (slot.Variants != null) for (int v = 0; v < slot.Variants.Count; v++) Visit(slot.Variants[v], i + 1, v + 1);
                if (slot.DlbVariants != null) for (int v = 0; v < slot.DlbVariants.Count; v++) Visit(slot.DlbVariants[v], i + 1, 1000 + v);
            }
            return issues;
        }
    }
}
