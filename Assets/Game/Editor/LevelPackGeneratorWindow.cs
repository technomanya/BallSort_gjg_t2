using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BallSortGame.Core;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace BallSortGame.EditorTools
{
    /// <summary>
    /// Fills each level design many times, rates every fill with <see cref="LevelRater"/> and keeps the fills that hit
    /// the target difficulty as the level's seeds. Writes a StageData JSON under Resources/data.
    /// </summary>
    public sealed class LevelPackGeneratorWindow : EditorWindow
    {
        [MenuItem("BallSort/Level Pack Generator")]
        public static void Open() => GetWindow<LevelPackGeneratorWindow>("Level Pack Generator");

        // ---- pack ----
        int _fromLevel = 1;
        int _toLevel = 50;
        int _seedsPerLevel = 5;
        int _capacity = 4;
        bool _useCurve = true;
        int _fixedColors = 9;
        int _fixedEmpties = 2;
        int _fixedBucket = 2;
        bool _hardCadence = true;
        bool _dlbForHard = true;
        int _dlbCap = 2;
        int _tolerance = 0;
        int _maxFills = 150;
        bool _fastBudget = true;
        int _hiddenTubes;
        int _hiddenFrom = 10;
        int _hiddenEvery = 1;
        int _baseSeed = 1;
        string _outputName = "gen_1_50";

        // ---- single rate ----
        string _ratePack = "data/levels";
        int _rateFrom = 1;
        int _rateTo = 20;

        Vector2 _scroll;
        string _log = "";

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Label("Generate a level pack", EditorStyles.boldLabel);
            _fromLevel = EditorGUILayout.IntField("From level", _fromLevel);
            _toLevel = EditorGUILayout.IntField("To level", _toLevel);
            _seedsPerLevel = EditorGUILayout.IntSlider("Seeds per level", _seedsPerLevel, 1, 10);
            _capacity = EditorGUILayout.IntSlider("Tube capacity", _capacity, 2, BoardState.MaxDepth);
            _useCurve = EditorGUILayout.Toggle(new GUIContent("Use progression curve", "Board size and target difficulty per level."), _useCurve);
            if (!_useCurve)
            {
                _fixedColors = EditorGUILayout.IntSlider("Colours", _fixedColors, 1, 16);
                _fixedEmpties = EditorGUILayout.IntSlider("Empty tubes", _fixedEmpties, 0, 4);
                _fixedBucket = EditorGUILayout.IntSlider("Target difficulty (1-7)", _fixedBucket, 1, 7);
            }
            _hardCadence = EditorGUILayout.Toggle(new GUIContent("Hard x4 / super-hard x9", "Levels ending in 4 target bucket 4, ending in 9 bucket 5."), _hardCadence);
            _dlbForHard = EditorGUILayout.Toggle(new GUIContent("Easier dlb seeds for hard levels", "Adds easier fills that take over after the cap of failed attempts."), _dlbForHard);
            if (_dlbForHard) _dlbCap = EditorGUILayout.IntSlider("dlb cap (failed attempts)", _dlbCap, 1, 10);
            _tolerance = EditorGUILayout.IntSlider(new GUIContent("Bucket tolerance", "Accept fills this many buckets away from the target."), _tolerance, 0, 2);
            _maxFills = EditorGUILayout.IntSlider("Max fills per level", _maxFills, 10, 1000);
            _fastBudget = EditorGUILayout.Toggle(new GUIContent("Fast solver budget", "Off = larger A* / playout budgets, more exact buckets, slower."), _fastBudget);
            _hiddenTubes = EditorGUILayout.IntSlider(new GUIContent("Hidden tubes per level", "Filled tubes covered by Art/Tubes/Hidden tube; one opens when a tube next to it is completed. Never two side by side."), _hiddenTubes, 0, 4);
            if (_hiddenTubes > 0)
            {
                _hiddenFrom = EditorGUILayout.IntField(new GUIContent("Hidden tubes from level", "First level that gets hidden tubes."), _hiddenFrom);
                _hiddenEvery = EditorGUILayout.IntSlider(new GUIContent("Hidden tubes every N levels", "1 = every level from the first one on, 3 = every third level, ..."), _hiddenEvery, 1, 10);
            }
            _baseSeed = EditorGUILayout.IntField("Random seed", _baseSeed);
            _outputName = EditorGUILayout.TextField("Output (Resources/data/)", _outputName);

            if (GUILayout.Button("Generate pack")) Generate();

            GUILayout.Space(12);
            GUILayout.Label("Rate an existing pack", EditorStyles.boldLabel);
            _ratePack = EditorGUILayout.TextField("Pack resource", _ratePack);
            _rateFrom = EditorGUILayout.IntField("From index (1-based)", _rateFrom);
            _rateTo = EditorGUILayout.IntField("To index", _rateTo);
            if (GUILayout.Button("Rate levels")) RatePack();

            GUILayout.Space(8);
            EditorGUILayout.HelpBox(_log.Length == 0 ? "Output appears here." : _log, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        void Generate()
        {
            var settings = new PackSettings
            {
                FromLevel = _fromLevel, ToLevel = _toLevel, SeedsPerLevel = _seedsPerLevel, Capacity = _capacity,
                UseCurve = _useCurve, FixedColors = _fixedColors, FixedEmpties = _fixedEmpties, FixedBucket = _fixedBucket,
                HardCadence = _hardCadence, DlbForHard = _dlbForHard, DlbCap = _dlbCap, Tolerance = _tolerance,
                MaxFills = _maxFills, FastBudget = _fastBudget, BaseSeed = _baseSeed, OutputName = _outputName,
                HiddenTubes = _hiddenTubes, HiddenFromLevel = _hiddenFrom, HiddenEvery = _hiddenEvery,
            };
            _log = GeneratePack(settings, true);
            Debug.Log(_log);
        }

        /// <summary>Generator inputs, so <see cref="GeneratePack"/> can also be run from a script.</summary>
        public sealed class PackSettings
        {
            public int FromLevel = 1, ToLevel = 50, SeedsPerLevel = 5, Capacity = 4;
            public bool UseCurve = true;
            public int FixedColors = 9, FixedEmpties = 2, FixedBucket = 2;
            public bool HardCadence = true, DlbForHard = true;
            public int DlbCap = 2, Tolerance = 0, MaxFills = 150;
            public bool FastBudget = true;
            /// <summary>Hidden tubes per level, from HiddenFromLevel on, every HiddenEvery levels.</summary>
            public int HiddenTubes = 0, HiddenFromLevel = 10, HiddenEvery = 1;
            public int BaseSeed = 1;
            public string OutputName = "gen_1_50";
        }

        /// <summary>Writes Resources/data/OutputName.json and returns the log.</summary>
        public static string GeneratePack(PackSettings s, bool showProgress = false)
        {
            var sb = new StringBuilder();
            var stage = new StageData { StageId = s.OutputName };
            var gen = new LevelGenerator();
            var budget = s.FastBudget ? RaterBudget.Fast : RaterBudget.Default;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int total = s.ToLevel - s.FromLevel + 1;
            try
            {
                for (int level = s.FromLevel; level <= s.ToLevel; level++)
                {
                    if (showProgress && EditorUtility.DisplayCancelableProgressBar("Generating levels", $"Level {level} / {s.ToLevel}", (level - s.FromLevel) / (float)total)) break;

                    int target;
                    bool hard = s.HardCadence && ProgressionCurve.IsHardLevel(level);
                    bool superHard = s.HardCadence && ProgressionCurve.IsSuperHardLevel(level);
                    var curveSlot = ProgressionCurve.For(level, s.Capacity);
                    LevelDesign NewDesign() => s.UseCurve
                        ? LevelDesign.Standard($"lvl{level}", curveSlot.Colors, curveSlot.Empties, s.Capacity, curveSlot.SpareTubes)
                        : LevelDesign.Standard($"lvl{level}", s.FixedColors, s.FixedEmpties, s.Capacity);
                    if (s.UseCurve) target = s.HardCadence ? curveSlot.TargetBucket : Math.Min(curveSlot.TargetBucket, 3);
                    else target = superHard ? Math.Max(s.FixedBucket, 5) : hard ? Math.Max(s.FixedBucket, 4) : s.FixedBucket;
                    if (level <= 4 && s.UseCurve) target = 1;
                    int wantHidden = s.HiddenTubes > 0 && level >= s.HiddenFromLevel && (level - s.HiddenFromLevel) % Math.Max(1, s.HiddenEvery) == 0
                        ? s.HiddenTubes : 0;

                    var opts = new GeneratorOptions
                    {
                        WantedBuckets = Enumerable.Repeat(target, s.SeedsPerLevel).ToList(),
                        Tolerance = s.Tolerance,
                        MaxFills = s.MaxFills,
                        Budget = budget,
                        BaseSeed = s.BaseSeed + level * 10_000,
                    };
                    // hidden tubes make boards harder: when no fill reaches the target, try again with one tube fewer
                    LevelDesign design;
                    List<GeneratedLevel> seeds;
                    string err;
                    int hiddenTubes;
                    for (int tryHidden = wantHidden; ; tryHidden--)
                    {
                        design = NewDesign();
                        hiddenTubes = tryHidden > 0 ? design.HideTubes(tryHidden, s.BaseSeed + level * 10_000 + 7_000) : 0;
                        seeds = gen.Generate(design, opts, out err);
                        if (seeds.Count > 0 || tryHidden <= 0) break;
                    }
                    if (seeds.Count == 0)
                    {
                        sb.AppendLine($"level {level}: no seed found ({err})");
                        continue;
                    }
                    var data = ToLevelData(seeds[0]);
                    data.IsHard = hard ? true : (bool?)null;
                    data.IsSuperHard = superHard ? true : (bool?)null;
                    if (seeds.Count > 1) data.Variants = seeds.Skip(1).Select(ToLevelData).ToList();

                    if (s.DlbForHard && (hard || superHard))
                    {
                        var dlbOpts = new GeneratorOptions
                        {
                            WantedBuckets = Enumerable.Repeat(Math.Max(1, target - 3), 3).ToList(),
                            Tolerance = 1,
                            MaxFills = s.MaxFills,
                            Budget = budget,
                            BaseSeed = s.BaseSeed + level * 10_000 + 5_000,
                        };
                        var dlb = gen.Generate(design, dlbOpts, out _);
                        if (dlb.Count > 0)
                        {
                            data.DlbVariants = dlb.Select(ToLevelData).ToList();
                            data.DlbCap = s.DlbCap;
                        }
                    }
                    stage.Levels.Add(data);
                    sb.AppendLine($"level {level}: {design.ColorCount}c/{design.TubeCount}t" + (hiddenTubes > 0 ? $"/{hiddenTubes}h" : "") + (hiddenTubes < wantHidden ? $" (wanted {wantHidden}h)" : "") + $" target {target} -> " +
                                  string.Join(" ", seeds.Select(x => $"{x.Id}({x.Rating.OptimalMoves}m)")) +
                                  (data.DlbVariants != null ? $" +{data.DlbVariants.Count} dlb" : "") +
                                  (err != null ? $"  [{err}]" : ""));
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }

            if (stage.Levels.Count > 0)
            {
                string dir = "Assets/Game/Resources/data";
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, s.OutputName + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(stage, Formatting.None));
                AssetDatabase.ImportAsset(path);
                sb.Insert(0, $"Wrote {stage.Levels.Count} levels to {path} in {sw.Elapsed.TotalSeconds:0.0} s\n");
            }
            return sb.ToString();
        }

        static LevelData ToLevelData(GeneratedLevel g) => new LevelData
        {
            PuzzleId = g.Id,
            Tubes = g.TubesTopFirst,
            TubeKinds = g.TubeKinds,
            Heavy = g.Heavy,
            Steps = g.Steps,
            Difficulty = g.Bucket,
        };

        void RatePack()
        {
            var stage = LevelLoader.LoadStage(_ratePack);
            if (stage == null) { _log = "pack not found: " + _ratePack; return; }
            var rater = new LevelRater();
            var sb = new StringBuilder();
            var budget = _fastBudget ? RaterBudget.Fast : RaterBudget.Default;
            int from = Mathf.Max(1, _rateFrom), to = Mathf.Min(stage.Levels.Count, _rateTo);
            var hist = new int[8];
            try
            {
                for (int i = from; i <= to; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Rating levels", $"{i} / {to}", (i - from) / (float)Mathf.Max(1, to - from))) break;
                    var lv = stage.Levels[i - 1];
                    var r = rater.Rate(lv.ToBoard(), budget);
                    if (r.Bucket >= 1 && r.Bucket <= 7) hist[r.Bucket]++;
                    sb.AppendLine($"{i,4} {lv.PuzzleId,-24} {r}");
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            sb.Insert(0, "buckets: " + string.Join(" ", Enumerable.Range(1, 7).Select(b => $"{b}:{hist[b]}")) + "\n");
            _log = sb.ToString();
            Debug.Log(_log);
        }
    }
}
