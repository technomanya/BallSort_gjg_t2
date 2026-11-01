using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BallSortGame.Core;
using BallSortGame.Gameplay;
using UnityEngine;

namespace BallSortGame.LevelEditor
{
    /// <summary>
    /// Plays the loaded level pack from a given level to the end (or one fill alone, <see cref="RunSingle"/>) on the
    /// real board, the way a player would: every
    /// level is audited (<see cref="LevelAudit"/>) and solved on a worker thread, then the bot taps the tubes of the
    /// solution through <see cref="GameManager.OnTubeClick"/> and waits for the animations like a finger would.
    /// The other fills of a slot (variants, dlb) are solver-checked in the background while the main fill is played.
    /// Everything that is wrong ends up in <see cref="Issues"/>: malformed or unsolvable fills, fills the solver
    /// cannot decide, stored steps that do not work, duplicate ids, and mismatches between the solver's rules and the
    /// game's (a solver move the board rejects, a "no moves" popup on a solvable line, a solution that does not win).
    /// </summary>
    public sealed class LevelBot : MonoBehaviour
    {
        public GameManager game;
        /// <summary>Time.timeScale while the bot plays.</summary>
        public float Speed = 4f;

        public bool IsRunning { get; private set; }
        /// <summary>The one fill being played alone (the editor's "this level"); null while the pack is played.</summary>
        public LevelData Single { get; private set; }
        /// <summary>Place name of <see cref="Single"/> in the status, the issues and the report.</summary>
        public string SingleName { get; private set; }
        public int FromLevel { get; private set; }
        public int CurrentLevel { get; private set; }
        public int LevelCount { get; private set; }
        public int Played { get; private set; }
        public int Won { get; private set; }
        public int Skipped { get; private set; }
        public bool Stopped { get; private set; }
        /// <summary>One line on what the bot is doing right now.</summary>
        public string Status { get; private set; } = "";
        public readonly List<LevelIssue> Issues = new List<LevelIssue>();
        /// <summary>One line per level for the full report (verdict, moves, bucket).</summary>
        public readonly List<string> Log = new List<string>();
        public event Action Finished;

        Coroutine _run;
        bool _stop, _noMoves;
        float _savedTimeScale = 1f;

        public int ErrorCount
        {
            get { int n = 0; foreach (var i in Issues) if (i.Severity == IssueSeverity.Error) n++; return n; }
        }

        public void Run(int fromLevel = 1)
        {
            if (IsRunning) return;
            Single = null;
            SingleName = null;
            FromLevel = Mathf.Max(1, fromLevel);
            Begin(RunRoutine());
        }

        /// <summary>Plays one fill alone (the editor's current level) as the override level, then reports on it.</summary>
        public void RunSingle(LevelData level, string name)
        {
            if (IsRunning || level == null) return;
            Single = level;
            SingleName = string.IsNullOrEmpty(name) ? "This level" : name;
            FromLevel = 1;
            Begin(RunSingleRoutine());
        }

        void Begin(IEnumerator routine)
        {
            Issues.Clear();
            Log.Clear();
            Played = Won = Skipped = 0;
            Stopped = _stop = false;
            IsRunning = true;
            _savedTimeScale = Time.timeScale;
            game.IsBotPlaying = true;
            game.NoMoves += OnNoMoves;
            _run = StartCoroutine(routine);
        }

        /// <summary>"Level 3/120" while the pack is played, the level's name when one fill is played alone.</summary>
        string Where(int level) => Single != null ? SingleName : $"Level {level}/{LevelCount}";

        /// <summary>Report / log prefix: "Level 3 (id)" or "name (id)".</summary>
        string LogName(int level, string id) => (Single != null ? SingleName : $"Level {level}") + (string.IsNullOrEmpty(id) ? "" : $" ({id})");

        /// <summary>Issues of a single run name the level, not "Level 1".</summary>
        void Tag(List<LevelIssue> issues)
        {
            if (Single == null) return;
            foreach (var i in issues) i.Label = SingleName;
        }

        /// <summary>Stops after the current move; the report then covers what was played so far.</summary>
        public void Stop() => _stop = true;

        void OnNoMoves() => _noMoves = true;

        void OnDisable()
        {
            if (IsRunning) Finish();
        }

        void Finish()
        {
            if (_run != null) { StopCoroutine(_run); _run = null; }
            IsRunning = false;
            Stopped = _stop;
            game.NoMoves -= OnNoMoves;
            game.IsBotPlaying = false;
            Time.timeScale = _savedTimeScale;
            Finished?.Invoke();
        }

        IEnumerator RunRoutine()
        {
            var stage = game.Stage;
            if (stage == null || stage.Levels.Count == 0)
            {
                Issues.Add(new LevelIssue { Severity = IssueSeverity.Error, Message = "no level pack is loaded" });
                Finish();
                yield break;
            }
            LevelCount = stage.Levels.Count;
            Issues.AddRange(LevelAudit.AuditPack(stage));

            for (int lv = FromLevel; lv <= LevelCount && !_stop; lv++)
                yield return AuditAndPlay(stage.Levels[lv - 1], lv);
            Status = _stop ? "Stopped." : "Done.";
            Finish();
        }

        IEnumerator RunSingleRoutine()
        {
            LevelCount = 1;
            yield return AuditAndPlay(Single, 1);
            Status = _stop ? "Stopped." : "Done.";
            Finish();
        }

        /// <summary>Shows a fill, audits and solves it on a worker thread, then plays the solution on the board.</summary>
        IEnumerator AuditAndPlay(LevelData slot, int lv)
        {
            Time.timeScale = Speed;
            CurrentLevel = lv;
            Status = $"{Where(lv)}: solving...";

            // show the level while the worker thread audits and solves it
            if (slot.Tubes != null && slot.Tubes.Count > 0 && slot.Capacity > 0)
            {
                if (Single != null) game.SetOverrideLevel(slot);
                else game.StartLevel(lv, 0);
            }

            AuditResult main = null;
            List<LevelIssue> others = null;
            var task = Task.Run(() =>
            {
                main = LevelAudit.Audit(slot, lv, 0, LevelAudit.PlayBudget);
                others = LevelAudit.AuditOtherSeeds(slot, lv, LevelAudit.CheckBudget);
            });
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                var e = task.Exception?.GetBaseException();
                Debug.LogException(e);
                Issues.Add(new LevelIssue { Level = lv, Label = SingleName, PuzzleId = slot.PuzzleId, Severity = IssueSeverity.Error, Message = "the audit crashed: " + e?.Message, Data = slot });
                Skipped++;
                yield break;
            }
            Tag(main.Issues);
            Tag(others);
            Issues.AddRange(main.Issues);
            Issues.AddRange(others);

            if (!main.Playable)
            {
                Skipped++;
                string why = main.Issues.Count > 0 ? main.Issues[0].Message : "not playable";
                Log.Add($"{LogName(lv, slot.PuzzleId)}: SKIPPED - {why}");
                Status = $"{Where(lv)}: {why}";
                yield return Wait(1.2f);   // leave the broken level on screen for a moment
                yield break;
            }

            Played++;
            yield return Play(slot, lv, main.Rating);
        }

        IEnumerator Play(LevelData slot, int level, LevelRating rating)
        {
            var solution = rating.Solution;
            _noMoves = false;
            string failure = null;
            int i = 0, played = 0, replans = 0;
            for (; i < solution.Count && !_stop; i++)
            {
                yield return WaitSettled();
                if (_noMoves) { failure = $"the game showed \"no moves\" after move {i} although the solver's line continues ({solution.Count} moves)"; break; }

                var m = solution[i];
                var tubes = game.Tubes;
                if (m.From >= tubes.Count || m.To >= tubes.Count) { failure = $"solver move {i + 1} names tube {Mathf.Max(m.From, m.To) + 1}, the board has {tubes.Count}"; break; }
                var from = tubes[m.From];
                var to = tubes[m.To];
                if (!to.CanImport(from)) { failure = $"the board rejects solver move {i + 1} ({m.From + 1} -> {m.To + 1}): rule mismatch between solver and game"; break; }
                int moving = Mathf.Min(from.TopBallSameCount, to.Capacity - to.Count);
                // a hidden ball ends a run in the game but not in the solver: play the shorter move, then re-solve from the board
                bool hiddenRun = moving != m.Count && from.HasHiddenBall;
                if (moving != m.Count && !hiddenRun) { failure = $"solver move {i + 1} ({m.From + 1} -> {m.To + 1}) moves {m.Count} balls, the board moves {moving}"; break; }

                game.OnTubeClick(from);
                if (!from.IsSelected) { failure = $"tube {m.From + 1} could not be selected for move {i + 1}"; break; }
                game.OnTubeClick(to);
                played++;
                Status = $"{Where(level)}: move {played}";
                if (hiddenRun)
                {
                    yield return WaitSettled();
                    if (++replans > 200) { failure = "too many re-plans around hidden balls"; break; }
                    var solver = new BallSortSolver();
                    if (solver.Solve(game.BuildBoard(), SolverBudget.Default) != SolveVerdict.Solvable || solver.Solution == null)
                    { failure = $"after {played} moves (hidden balls shortened a run) the solver finds no solution from the board"; break; }
                    solution = solver.Solution;
                    i = -1;
                }
            }
            yield return WaitSettled();
            if (_stop && failure == null) yield break;

            string id = slot.PuzzleId;
            if (failure != null)
            {
                Issues.Add(new LevelIssue { Level = level, Label = SingleName, PuzzleId = id, Severity = IssueSeverity.Error, Message = failure, Data = slot });
                Log.Add($"{LogName(level, id)}: FAILED after {i} moves - {failure}");
                yield return Wait(1.0f);
                yield break;
            }
            if (game.GameOver())
            {
                Won++;
                Log.Add($"{LogName(level, id)}: won in {played} moves, bucket {rating.Bucket}, {rating.Tubes} tubes {rating.Colors} colours");
                Status = $"{Where(level)}: won in {played} moves";
                yield return Wait(game.WinDelay + 0.3f);   // let the "Well done" popup show like in play
            }
            else
            {
                string board = game.BuildBoard().ToString();
                Issues.Add(new LevelIssue { Level = level, Label = SingleName, PuzzleId = id, Severity = IssueSeverity.Error, Message = $"the solver's solution ({played} moves played) did not finish the level; board after it: {board}", Data = slot });
                Log.Add($"{LogName(level, id)}: NOT WON after the solution");
                yield return Wait(1.0f);
            }
        }

        IEnumerator WaitSettled()
        {
            yield return null;
            while (game.AnyTubeMoving() && !_stop) yield return null;
        }

        IEnumerator Wait(float seconds)
        {
            float end = Time.unscaledTime + seconds / Mathf.Max(1f, Speed);
            while (Time.unscaledTime < end && !_stop) yield return null;
        }

        public string Summary()
        {
            int errors = ErrorCount, warnings = Issues.Count - errors;
            string range = Single != null ? SingleName : FromLevel > 1 ? $"levels {FromLevel}-{LevelCount}" : $"{LevelCount} levels";
            return $"{range}: {Won} won, {Skipped} skipped, {Played - Won} failed, {errors} error(s), {warnings} warning(s)" + (Stopped ? " (stopped)" : "");
        }

        /// <summary>Full text report: summary, issues, then the per-level log.</summary>
        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Level bot report - " + Summary());
            sb.AppendLine(Issues.Count == 0 ? "No issues." : $"{Issues.Count} issue(s):");
            foreach (var issue in Issues) sb.AppendLine("  " + issue);
            sb.AppendLine();
            foreach (var line in Log) sb.AppendLine(line);
            return sb.ToString();
        }
    }
}
