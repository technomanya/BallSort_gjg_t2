using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BallSortGame.Core;
using BallSortGame.Gameplay;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BallSortGame.LevelEditor
{
    public enum EditorTool { Paint, Fill, Swap, Delete }

    /// <summary>
    /// In-app level editor, the ball-sort counterpart of Magic Sort's Modules.LevelEditor.WaterSort (EditorManager,
    /// EditorInputSystem, DrawBottle/DrawPreset buttons, ColorPalette, Test/Save/Load buttons). It ships inside the
    /// normal game build: it attaches itself to any scene with a <see cref="GameMgr"/> when <see cref="IsAllowed"/>
    /// (Unity Editor, development builds, or the PlayerPrefs switch), and designers open it with F2 or five quick taps
    /// on the level pill. Players of a release build never see it.
    ///
    /// Flow: draw tubes (fixed colours, "?" luck slots, empty) -> Test plays a random fill in the real game ->
    /// Generate makes rated seeds with <see cref="LevelGenerator"/> (the local stand-in for Magic Sort's
    /// SolveLevelRequest backend) -> To Pack writes them into Resources/data/editor_pack.json.
    /// </summary>
    public class LevelEditorManager : MonoBehaviour
    {
        /// <summary>PlayerPrefs int; 1 unlocks the editor in release builds (set it from a debug menu or adb).</summary>
        public const string AccessKey = "ballsort.level_editor";
        const int Eraser = 0;
        const int MaxPaletteColors = 14;
        const int UndoDepth = 100;

        public static bool IsAllowed => Application.isEditor || Debug.isDebugBuild || PlayerPrefs.GetInt(AccessKey, 0) == 1;
        public static string SaveDir => Path.Combine(Application.persistentDataPath, "LevelEditor");
        /// <summary>In the Unity Editor the pack goes into Resources so GameMgr can load it as "data/editor_pack".</summary>
        public static string PackPath => Application.isEditor
            ? Path.Combine(Application.dataPath, "Game/Resources/data/editor_pack.json")
            : Path.Combine(SaveDir, "editor_pack.json");

        public GameMgr game;
        public bool IsOpen => _open;
        public bool IsTesting => _testing;

        EditorLevel _level;
        int _selected = 1;              // palette value: colour > 0, EditorLevel.Random, or Eraser
        EditorTool _tool = EditorTool.Paint;
        int _swapFrom = -1;
        int _packLevel = 1;
        bool _open, _testing;
        readonly List<string> _undo = new List<string>();
        readonly List<string> _redo = new List<string>();

        // secret entry
        int _taps;
        float _lastTap;

        // background job (solver / generator run off the main thread)
        Task _job;
        Action _jobDone;
        bool _generating;
        volatile int _progFills, _progSeeds;
        int _genBucket = 2, _genCount = 5;

        // UI
        Canvas _canvas;
        CanvasScaler _scaler;
        GameObject _panel, _testBar, _modal;
        RectTransform _board, _modalWindow, _genList;
        Vector2 _boardSize;
        Text _info, _capText, _colorsText, _packText, _genStatus;
        InputField _nameField;
        Button _undoButton, _redoButton;
        readonly Dictionary<EditorTool, Image> _toolImages = new Dictionary<EditorTool, Image>();
        readonly List<Swatch> _swatches = new List<Swatch>();

        struct Swatch
        {
            public int Value;
            public Image Bg, Icon;
            public Text Usage;
            public Color IconColor;
        }

        // =====================================================================
        // Bootstrap: attach to every gameplay scene when allowed
        // =====================================================================

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

        static void Attach()
        {
            if (!IsAllowed || FindFirstObjectByType<LevelEditorManager>() != null) return;
            var game = FindFirstObjectByType<GameMgr>();
            if (game == null) return;
            new GameObject("LevelEditor").AddComponent<LevelEditorManager>().game = game;
        }

        void Update()
        {
            PollJob();
            // keep the canvas at least 1080x2340 units in portrait and landscape windows
            if (_scaler != null) _scaler.matchWidthOrHeight = Screen.width * 2340f > Screen.height * 1080f ? 1f : 0f;

            if (_testing)
            {
                if (Input.GetKeyDown(KeyCode.F2) || Input.GetKeyDown(KeyCode.Escape)) BackToEditor();
                return;
            }
            if (_open)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (_modal.activeSelf) CloseModal();
                    else Close();
                }
                else if (Input.GetKeyDown(KeyCode.F2)) Close();
                else if (!IsTyping() && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                {
                    if (Input.GetKeyDown(KeyCode.Z)) Undo();
                    else if (Input.GetKeyDown(KeyCode.Y)) Redo();
                }
                return;
            }
            if (Input.GetKeyDown(KeyCode.F2)) { Open(); return; }
            DetectSecretTaps();
        }

        void LateUpdate()
        {
            if (_open && _board != null && _board.rect.size != _boardSize) RebuildBoard();
        }

        /// <summary>Five quick taps on the level pill (top centre of the screen) open the editor.</summary>
        void DetectSecretTaps()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            Vector2 p = Input.mousePosition;
            if (p.y < Screen.height * 0.88f || Mathf.Abs(p.x - Screen.width * 0.5f) > Screen.width * 0.2f) { _taps = 0; return; }
            if (Time.unscaledTime - _lastTap > 0.6f) _taps = 0;
            _lastTap = Time.unscaledTime;
            if (++_taps >= 5)
            {
                _taps = 0;
                Open();
            }
        }

        static bool IsTyping()
        {
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return sel != null && sel.TryGetComponent(out InputField f) && f.isFocused;
        }

        // =====================================================================
        // Open / close / test (Magic Sort GamePlaySceneToEditorSceneCommand / TestButton / ReturnToEditorSceneButton)
        // =====================================================================

        public void Open()
        {
            if (game == null) return;
            if (_canvas == null) BuildUI();
            if (_level == null) _level = StartingLayout();
            _open = true;
            _testing = false;
            game.IsPaused = true;
            _panel.SetActive(true);
            _testBar.SetActive(false);
            if (game.Stage != null) _packLevel = Mathf.Clamp(game.CurrentLevel, 1, game.Stage.Levels.Count);
            Canvas.ForceUpdateCanvases();   // a freshly built canvas has no size until it is laid out once
            Refresh();
        }

        public void Close()
        {
            _open = _testing = false;
            CloseModal();
            _panel.SetActive(false);
            _testBar.SetActive(false);
            if (game.OverrideLevel != null) game.SetOverrideLevel(null);   // back to the pack level that was being played
            game.IsPaused = false;
        }

        EditorLevel StartingLayout()
        {
            var cur = game.CurrentLevelData;
            if (cur != null && cur.Tubes.Count > 0) return EditorLevel.FromLevelData(cur, $"lv{Mathf.Max(1, game.CurrentLevel)}_edit");
            return EditorLevel.Standard(NewName(), 4, 2, 4);
        }

        /// <summary>Plays the generated seeds when there are any, otherwise one random fill of the layout.</summary>
        void Test()
        {
            var data = _level.Seeds;
            if (data == null)
            {
                if (!_level.Validate(out var error)) { Info(error, EditorUI.Bad); return; }
                int seed = UnityEngine.Random.Range(1, int.MaxValue);
                var board = _level.Fill(seed, out error);
                if (board == null) { Info(error, EditorUI.Bad); return; }
                data = new LevelData { PuzzleId = $"{_level.Name}_seed{seed}", Tubes = LevelFiller.ToTopFirst(board) };
            }
            PlayTest(data);
        }

        void PlayTest(LevelData data)
        {
            CloseModal();
            _testing = true;
            _panel.SetActive(false);
            _testBar.SetActive(true);
            game.SetOverrideLevel(data);
        }

        void BackToEditor()
        {
            _testing = false;
            _testBar.SetActive(false);
            _panel.SetActive(true);
            game.IsPaused = true;
            Refresh();
        }

        // =====================================================================
        // Editing with undo (Magic Sort CommandHistoryManager)
        // =====================================================================

        /// <summary>Undo state: the layout with its seeds, so undoing an edit brings the generated seeds back.</summary>
        string Snapshot() => JsonConvert.SerializeObject(_level);

        /// <summary>The layout alone, to tell whether seeds still belong to it.</summary>
        string LayoutKey()
        {
            var seeds = _level.Seeds;
            _level.Seeds = null;
            var json = JsonConvert.SerializeObject(_level);
            _level.Seeds = seeds;
            return json;
        }

        /// <summary>Runs an edit; when it changed something the old state goes on the undo stack and the seeds are dropped.</summary>
        void Edit(Func<bool> op)
        {
            var before = Snapshot();
            if (!op()) return;
            _undo.Add(before);
            if (_undo.Count > UndoDepth) _undo.RemoveAt(0);
            _redo.Clear();
            _level.Seeds = null;
            Refresh();
        }

        void Undo() => Step(_undo, _redo);
        void Redo() => Step(_redo, _undo);

        void Step(List<string> from, List<string> to)
        {
            if (from.Count == 0) return;
            to.Add(Snapshot());
            _level = JsonConvert.DeserializeObject<EditorLevel>(from[from.Count - 1]);
            from.RemoveAt(from.Count - 1);
            _swapFrom = -1;
            Refresh();
        }

        void Replace(EditorLevel level) => Edit(() => { _level = level; return true; });

        static string NewName() => "custom_" + DateTime.Now.ToString("MMdd_HHmmss");

        int PaletteSize => Mathf.Clamp(game.ballSprites != null && game.ballSprites.Length > 0 ? game.ballSprites.Length : 12, 1, MaxPaletteColors);

        // =====================================================================
        // Input on the board (Magic Sort EditorInputSystem)
        // =====================================================================

        void OnCellClicked(int tube, int slot)
        {
            if (_tool == EditorTool.Paint) Edit(() => _level.Paint(tube, slot, _selected));
            else OnTubeClicked(tube);
        }

        void OnTubeClicked(int tube)
        {
            switch (_tool)
            {
                case EditorTool.Paint:
                    // tapping the glass above the balls stacks onto the tube; the eraser takes the top ball
                    int count = _level.Tubes[tube].Count;
                    Edit(() => _selected == Eraser ? _level.Paint(tube, count - 1, Eraser) : _level.Paint(tube, count, _selected));
                    break;
                case EditorTool.Fill:
                    Edit(() => _level.FillTube(tube, _selected));
                    break;
                case EditorTool.Swap:
                    if (_swapFrom < 0) { _swapFrom = tube; RebuildBoard(); }
                    else
                    {
                        int a = _swapFrom;
                        _swapFrom = -1;
                        if (a == tube) RebuildBoard();
                        else Edit(() => _level.SwapTubes(a, tube));
                    }
                    break;
                case EditorTool.Delete:
                    Edit(() => _level.RemoveTube(tube));
                    break;
            }
        }

        void SetTool(EditorTool tool)
        {
            _tool = tool;
            _swapFrom = -1;
            RefreshTools();
            RebuildBoard();
            Info(tool switch
            {
                EditorTool.Paint => "Paint: tap a slot to place the selected colour or ?, or erase it.",
                EditorTool.Fill => "Fill tube: tap a tube to fill it with the selected value (? = luck tube, Erase = empty tube).",
                EditorTool.Swap => "Move: tap two tubes to swap their places.",
                _ => "Delete: tap a tube to remove it.",
            }, Color.white);
        }

        void SelectValue(int value)
        {
            _selected = value;
            if (_tool == EditorTool.Swap || _tool == EditorTool.Delete) SetTool(EditorTool.Paint);
            RefreshPalette();
        }

        // =====================================================================
        // Actions
        // =====================================================================

        void LoadPackLevel()
        {
            var stage = game.Stage;
            if (stage == null || stage.Levels.Count == 0) { Info("No level pack loaded.", EditorUI.Bad); return; }
            var data = stage.Levels[(_packLevel - 1) % stage.Levels.Count];
            Replace(EditorLevel.FromLevelData(data, $"lv{_packLevel}_edit"));
            Info($"Loaded level {_packLevel} ({data.PuzzleId}) of {game.levelPackResource} as a fixed layout.", EditorUI.Good);
        }

        void CopyLayout()
        {
            GUIUtility.systemCopyBuffer = JsonConvert.SerializeObject(_level, Formatting.Indented);
            Info("Layout JSON copied to the clipboard.", EditorUI.Good);
        }

        void PasteLayout()
        {
            if (!EditorLevel.TryParse(GUIUtility.systemCopyBuffer ?? "", out var level, out var error))
            {
                Info("Clipboard: " + error, EditorUI.Bad);
                return;
            }
            var seeds = level.Seeds;
            Replace(level);
            _level.Seeds = seeds;
            RefreshInfo();
        }

        void SaveLayout()
        {
            try
            {
                Directory.CreateDirectory(SaveDir);
                string path = Path.Combine(SaveDir, SafeFileName(_level.Name) + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(_level, Formatting.Indented));
                Info("Saved " + path, EditorUI.Good);
            }
            catch (Exception e)
            {
                Info("Save failed: " + e.Message, EditorUI.Bad);
            }
        }

        static string SafeFileName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        void LoadFile(string path)
        {
            if (!EditorLevel.TryParse(File.ReadAllText(path), out var level, out var error))
            {
                Info(Path.GetFileName(path) + ": " + error, EditorUI.Bad);
                return;
            }
            CloseModal();
            var seeds = level.Seeds;   // saved seeds belong to the saved layout, keep them
            Replace(level);
            _level.Seeds = seeds;
            RefreshInfo();
        }

        /// <summary>Rates one fill: solvable, optimal-ish move count, and the bot difficulty bucket.</summary>
        void CheckSolvable()
        {
            if (!_level.Validate(out var error)) { Info(error, EditorUI.Bad); return; }
            int seed = _level.HasRandom ? UnityEngine.Random.Range(1, int.MaxValue) : 0;
            var board = _level.Fill(seed, out error);
            if (board == null) { Info(error, EditorUI.Bad); return; }
            string what = _level.HasRandom ? $"Random fill (seed {seed})" : "Layout";
            LevelRating rating = null;
            RunJob("Checking with the solver and the playout bot...", () => rating = new LevelRater().Rate(board, RaterBudget.Default), () =>
            {
                if (rating.Verdict == SolveVerdict.Solvable)
                    Info($"{what}: solvable in {rating.OptimalMoves} moves, difficulty {rating.Bucket}/7 (bot loses {rating.Score:P0}).", EditorUI.Good);
                else
                    Info(rating.Verdict == SolveVerdict.Unsolvable ? $"{what}: unsolvable." : $"{what}: the solver ran out of budget.", EditorUI.Bad);
            });
        }

        void AddToPack()
        {
            var seeds = _level.Seeds;
            if (seeds == null) { Info("Generate first: the pack stores rated seeds.", EditorUI.Bad); return; }
            try
            {
                StageData stage = File.Exists(PackPath) ? JsonConvert.DeserializeObject<StageData>(File.ReadAllText(PackPath)) : null;
                if (stage == null) stage = new StageData { StageId = "editor_pack" };
                string name = _level.Name;
                int idx = stage.Levels.FindIndex(l => l.PuzzleId == name || (l.PuzzleId != null && l.PuzzleId.StartsWith(name + "_difficulty")));
                bool replaced = idx >= 0;
                if (replaced) stage.Levels[idx] = seeds;
                else { stage.Levels.Add(seeds); idx = stage.Levels.Count - 1; }
                Directory.CreateDirectory(Path.GetDirectoryName(PackPath));
                File.WriteAllText(PackPath, JsonConvert.SerializeObject(stage));
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                string where = Application.isEditor ? "data/editor_pack" : PackPath;
                Info($"{(replaced ? "Updated" : "Added")} level {idx + 1} of {where} ({stage.Levels.Count} levels).", EditorUI.Good);
            }
            catch (Exception e)
            {
                Info("Pack write failed: " + e.Message, EditorUI.Bad);
            }
        }

        // =====================================================================
        // Seed generation (the local stand-in for Magic Sort's SolveLevelRequest backend)
        // =====================================================================

        void RunGenerate()
        {
            if (_job != null) { GenStatus("A job is already running.", EditorUI.Bad); return; }
            if (!_level.Validate(out var error)) { GenStatus(error, EditorUI.Bad); return; }
            var design = _level.ToDesign();
            var layout = LayoutKey();
            bool random = _level.HasRandom;
            int bucket = _genBucket, count = random ? _genCount : 1;
            int baseSeed = UnityEngine.Random.Range(1, 1_000_000);
            var results = new List<GeneratedLevel>();
            string genError = null;
            _progFills = _progSeeds = 0;
            _generating = true;
            RunJob(random ? "Generating seeds..." : "Rating the layout...", () =>
            {
                var gen = new LevelGenerator { Progress = (fills, accepted, wanted) => { _progFills = fills; _progSeeds = accepted; } };
                if (random)
                {
                    var options = new GeneratorOptions
                    {
                        WantedBuckets = Enumerable.Repeat(bucket, count).ToList(),
                        FallbackTolerance = 6,   // any solvable fill is better than none; the bucket is shown per seed
                        Budget = RaterBudget.Fast,
                        BaseSeed = baseSeed,
                    };
                    results = gen.Generate(design, options, out genError);
                }
                else
                {
                    var board = LevelFiller.Fill(design, 0, out genError);
                    var rating = gen.Rate(board, RaterBudget.Default);
                    if (rating.Verdict == SolveVerdict.Solvable)
                        results.Add(new GeneratedLevel { Id = design.Name, DesignName = design.Name, Board = board, Rating = rating, Bucket = rating.Bucket, Variant = 1 });
                    else
                        genError = rating.Verdict == SolveVerdict.Unsolvable ? "The layout is unsolvable." : "The solver could not decide within its budget.";
                }
            }, () =>
            {
                _generating = false;
                if (LayoutKey() != layout) { GenStatus("The layout changed while generating; run again.", EditorUI.Bad); return; }
                if (results.Count == 0) { GenStatus(genError ?? "No solvable fill found.", EditorUI.Bad); return; }
                _level.Seeds = BuildSeeds(results);
                GenStatus($"{results.Count} seed(s) ready." + (genError != null ? " " + genError : ""), genError == null ? EditorUI.Good : Color.white);
                ShowResults();
                RefreshInfo();
            });
        }

        static LevelData BuildSeeds(List<GeneratedLevel> results)
        {
            LevelData Make(GeneratedLevel g) => new LevelData { PuzzleId = g.Id, Tubes = g.TubesTopFirst, Steps = g.Steps, Difficulty = g.Bucket };
            var main = Make(results[0]);
            if (results.Count > 1) main.Variants = results.Skip(1).Select(Make).ToList();
            return main;
        }

        void RunJob(string text, Action work, Action done)
        {
            if (_job != null) { Info("A job is already running.", EditorUI.Bad); return; }
            _jobDone = done;
            Info(text, Color.white);
            _job = Task.Run(work);
        }

        void PollJob()
        {
            if (_job == null) return;
            if (!_job.IsCompleted)
            {
                if (_generating && _genStatus != null) _genStatus.text = $"Generating... {_progFills} fills tried, {_progSeeds}/{_genCount} seeds";
                return;
            }
            var job = _job;
            _job = null;
            if (job.IsFaulted)
            {
                _generating = false;
                var e = job.Exception?.GetBaseException();
                Debug.LogException(e);
                Info("Job failed: " + e?.Message, EditorUI.Bad);
                GenStatus("Job failed: " + e?.Message, EditorUI.Bad);
                return;
            }
            RefreshInfo();
            _jobDone?.Invoke();
        }

        // =====================================================================
        // UI construction
        // =====================================================================

        void BuildUI()
        {
            var canvasGo = new GameObject("LevelEditorCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;   // above the game HUD
            _scaler = canvasGo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1080, 2340);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            if (FindFirstObjectByType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var root = (RectTransform)canvasGo.transform;

            // ---- editor panel (opaque, covers the game)
            var panel = EditorUI.Image("Editor", root, null, EditorUI.Background);
            EditorUI.Stretch(panel.rectTransform, Vector2.zero, Vector2.zero);
            _panel = panel.gameObject;
            var p = panel.transform;

            var top = EditorUI.Band("top_bar", p, true, 0f, 130f, 0f);
            top.gameObject.AddComponent<Image>().color = EditorUI.Bar;
            EditorUI.RowLayout(top.gameObject, 14f).padding = new RectOffset(24, 24, 16, 16);
            EditorUI.Button(top, "Close", EditorUI.Danger, Close, 40, 1f);
            EditorUI.Label(EditorUI.Flex(EditorUI.Rect("title", top), 2.4f), "LEVEL EDITOR", 54, TextAnchor.MiddleCenter, Color.white);
            EditorUI.Button(top, "Test", EditorUI.Go, Test, 46, 1.2f);

            var row1 = EditorUI.Row("settings_1", p, true, 150f, 100f);
            _nameField = EditorUI.Input(row1, "level name", v => { v = v.Trim(); if (v.Length > 0) _level.Name = v; }, 3f);
            _capText = EditorUI.Stepper(row1, "Height", d => Edit(() => _level.SetCapacity(_level.Capacity + d)), 3.2f);

            var row2 = EditorUI.Row("settings_2", p, true, 262f, 100f);
            _colorsText = EditorUI.Stepper(row2, "Colours", d => Edit(() => _level.SetColorCount(_level.ColorCount + d, PaletteSize)), 3.2f);
            _packText = EditorUI.Stepper(row2, "Pack lv", d =>
            {
                int n = game.Stage != null ? game.Stage.Levels.Count : 1;
                _packLevel = (_packLevel - 1 + d + n) % n + 1;
                _packText.text = _packLevel.ToString();
            }, 3.2f);
            EditorUI.Button(row2, "Load", EditorUI.ButtonColor, LoadPackLevel, 40, 1.3f);

            var infoBand = EditorUI.Band("info", p, true, 374f, 92f);
            infoBand.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            _info = EditorUI.Label(infoBand, "", 34, TextAnchor.MiddleLeft, Color.white, FontStyle.Normal);
            _info.rectTransform.offsetMin = new Vector2(20f, 0f);

            _board = EditorUI.Stretch(EditorUI.Rect("board", p), new Vector2(24f, 650f), new Vector2(-24f, -480f));

            BuildPalette(p);

            var tools = EditorUI.Row("tools", p, false, 280f, 100f);
            AddTool(tools, EditorTool.Paint, "Paint");
            AddTool(tools, EditorTool.Fill, "Fill tube");
            AddTool(tools, EditorTool.Swap, "Move");
            AddTool(tools, EditorTool.Delete, "Delete");
            EditorUI.Button(tools, "+ Tube", EditorUI.Go, () => Edit(_level.AddTube), 40);

            var actions = EditorUI.Row("actions_1", p, false, 160f, 100f);
            _undoButton = EditorUI.Button(actions, "Undo", EditorUI.ButtonColor, Undo, 38);
            _redoButton = EditorUI.Button(actions, "Redo", EditorUI.ButtonColor, Redo, 38);
            EditorUI.Button(actions, "New", EditorUI.ButtonColor, () => Replace(EditorLevel.Standard(NewName(), Mathf.Min(_level.ColorCount, PaletteSize), 2, _level.Capacity)), 38);
            EditorUI.Button(actions, "Clear", EditorUI.ButtonColor, () => Edit(_level.ClearBalls), 38);
            EditorUI.Button(actions, "Check", EditorUI.ButtonColor, CheckSolvable, 38);
            EditorUI.Button(actions, "Generate", EditorUI.Accent, OpenGenerate, 38, 1.4f);

            var files = EditorUI.Row("actions_2", p, false, 40f, 100f);
            EditorUI.Button(files, "Copy", EditorUI.ButtonColor, CopyLayout, 38);
            EditorUI.Button(files, "Paste", EditorUI.ButtonColor, PasteLayout, 38);
            EditorUI.Button(files, "Save", EditorUI.ButtonColor, SaveLayout, 38);
            EditorUI.Button(files, "Open", EditorUI.ButtonColor, OpenFiles, 38);
            EditorUI.Button(files, "To pack", EditorUI.Accent, AddToPack, 38, 1.3f);

            // ---- modal window (generate / open)
            var modal = EditorUI.Image("Modal", p, null, new Color(0f, 0f, 0f, 0.7f));
            EditorUI.Stretch(modal.rectTransform, Vector2.zero, Vector2.zero);
            var window = EditorUI.Image("window", modal.transform, EditorUI.RoundRect, EditorUI.Bar, true);
            _modalWindow = EditorUI.Stretch(window.rectTransform, new Vector2(40f, 260f), new Vector2(-40f, -200f));
            _modal = modal.gameObject;
            _modal.SetActive(false);

            // ---- floating "back" button shown while testing
            var back = EditorUI.Button(root, "< Editor", EditorUI.Accent, BackToEditor, 44);
            var brt = (RectTransform)back.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.anchoredPosition = new Vector2(0f, -190f);
            brt.sizeDelta = new Vector2(340f, 96f);
            _testBar = back.gameObject;
            _testBar.SetActive(false);
        }

        void AddTool(Transform row, EditorTool tool, string label)
        {
            var b = EditorUI.Button(row, label, EditorUI.ButtonColor, () => SetTool(tool), 38);
            _toolImages[tool] = (Image)b.targetGraphic;
        }

        void BuildPalette(Transform parent)
        {
            var band = EditorUI.Band("palette", parent, false, 400f, 236f);
            var grid = band.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(112f, 112f);
            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 8;
            grid.childAlignment = TextAnchor.MiddleCenter;

            var values = new List<int>();
            for (int c = 1; c <= PaletteSize; c++) values.Add(c);
            values.Add(EditorLevel.Random);
            values.Add(Eraser);
            foreach (int v in values)
            {
                var bg = EditorUI.Image("swatch " + v, band, EditorUI.RoundRect, EditorUI.ButtonColor, true);
                var b = bg.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                int value = v;
                b.onClick.AddListener(() => SelectValue(value));
                var icon = BallImage(bg.transform, v, 70f);
                icon.rectTransform.anchoredPosition = new Vector2(0f, 12f);
                if (v == Eraser) EditorUI.Label(icon.transform, "Erase", 24, TextAnchor.MiddleCenter, Color.white);
                var usageRt = EditorUI.Rect("usage", bg.transform);
                usageRt.anchorMin = new Vector2(0f, 0f);
                usageRt.anchorMax = new Vector2(1f, 0f);
                usageRt.pivot = new Vector2(0.5f, 0f);
                usageRt.sizeDelta = new Vector2(0f, 32f);
                usageRt.anchoredPosition = new Vector2(0f, 2f);
                var usage = EditorUI.Label(usageRt, "", 26, TextAnchor.MiddleCenter, Color.white);
                _swatches.Add(new Swatch { Value = v, Bg = bg, Icon = icon, Usage = usage, IconColor = icon.color });
            }
        }

        Image BallImage(Transform parent, int value, float size)
        {
            var img = EditorUI.Image("ball", parent, EditorUI.Circle, Color.white);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.preserveAspect = true;
            if (value > 0)
            {
                var sprite = game.BallSpriteFor(value);
                if (sprite != null) img.sprite = sprite;
                else img.color = Color.HSVToRGB((value - 1) * 0.618f % 1f, 0.7f, 0.95f);
            }
            else if (value == EditorLevel.Random)
            {
                img.color = new Color(0.60f, 0.60f, 0.68f, 1f);
                EditorUI.Label(img.transform, "?", Mathf.RoundToInt(size * 0.6f), TextAnchor.MiddleCenter, Color.white);
            }
            else img.color = new Color(0f, 0f, 0f, 0.35f);
            return img;
        }

        // =====================================================================
        // Modals
        // =====================================================================

        RectTransform OpenModal(string title)
        {
            _modal.SetActive(true);
            for (int i = _modalWindow.childCount - 1; i >= 0; i--) Destroy(_modalWindow.GetChild(i).gameObject);
            _genStatus = null;
            _genList = null;
            var head = EditorUI.Row("head", _modalWindow, true, 20f, 100f);
            EditorUI.Label(EditorUI.Flex(EditorUI.Rect("title", head), 4f), title, 50, TextAnchor.MiddleLeft, Color.white);
            EditorUI.Button(head, "Close", EditorUI.ButtonColor, CloseModal, 40, 1.4f);
            return _modalWindow;
        }

        void CloseModal()
        {
            if (_modal != null) _modal.SetActive(false);
            _genStatus = null;
            _genList = null;
        }

        void OpenFiles()
        {
            var win = OpenModal("Open layout");
            var list = EditorUI.ScrollList(win, new Vector2(24f, 24f), new Vector2(-24f, -136f));
            var files = Directory.Exists(SaveDir)
                ? new DirectoryInfo(SaveDir).GetFiles("*.json").Where(f => f.Name != "editor_pack.json").OrderByDescending(f => f.LastWriteTime).ToList()
                : new List<FileInfo>();
            if (files.Count == 0) EditorUI.ListButton(list, "No saved layouts in " + SaveDir, new Color(0f, 0f, 0f, 0f), null).interactable = false;
            foreach (var f in files)
            {
                string path = f.FullName;
                EditorUI.ListButton(list, $"{Path.GetFileNameWithoutExtension(f.Name)}    {f.LastWriteTime:yyyy-MM-dd HH:mm}", EditorUI.ButtonColor, () => LoadFile(path));
            }
        }

        void OpenGenerate()
        {
            var win = OpenModal("Generate seeds");
            var row1 = EditorUI.Row("options", win, true, 140f, 100f);
            Text bucketText = null, countText = null;
            bucketText = EditorUI.Stepper(row1, "Difficulty", d => { _genBucket = Mathf.Clamp(_genBucket + d, 1, 7); bucketText.text = _genBucket.ToString(); });
            countText = EditorUI.Stepper(row1, "Seeds", d => { _genCount = Mathf.Clamp(_genCount + d, 1, 10); countText.text = _genCount.ToString(); });
            bucketText.text = _genBucket.ToString();
            countText.text = _genCount.ToString();

            var row2 = EditorUI.Row("run", win, true, 256f, 100f);
            EditorUI.Button(row2, "Run", EditorUI.Go, RunGenerate, 42);
            EditorUI.Button(row2, "Test seeds", EditorUI.ButtonColor, () => { if (_level.Seeds != null) PlayTest(_level.Seeds); else GenStatus("Run first.", EditorUI.Bad); }, 38);
            EditorUI.Button(row2, "To pack", EditorUI.Accent, AddToPack, 38);

            var status = EditorUI.Band("status", win, true, 372f, 90f);
            _genStatus = EditorUI.Label(status, "", 34, TextAnchor.MiddleLeft, Color.white, FontStyle.Normal);
            _genList = EditorUI.ScrollList(win, new Vector2(24f, 24f), new Vector2(-24f, -478f));

            GenStatus(_level.HasRandom
                ? "Fills the ? slots at random, rates every fill with the playout bot (1 easy .. 7 very hard) and keeps the wanted difficulty."
                : "No ? slots: Run rates the fixed layout once.", EditorUI.Muted);
            ShowResults();
        }

        void GenStatus(string text, Color color)
        {
            if (_genStatus == null) { Info(text, color); return; }
            _genStatus.text = text;
            _genStatus.color = color;
        }

        void ShowResults()
        {
            if (_genList == null) return;
            for (int i = _genList.childCount - 1; i >= 0; i--) Destroy(_genList.GetChild(i).gameObject);
            var seeds = _level.Seeds;
            if (seeds == null) return;
            for (int i = 0; i < seeds.SeedCount; i++)
            {
                var s = seeds.SeedAt(i);
                var single = new LevelData { PuzzleId = s.PuzzleId, Tubes = s.Tubes, Steps = s.Steps, Difficulty = s.Difficulty };
                string label = $"Play   {s.PuzzleId}    difficulty {s.Difficulty}    {s.Steps?.Count ?? 0} moves";
                EditorUI.ListButton(_genList, label, EditorUI.ButtonColor, () => PlayTest(single));
            }
        }

        // =====================================================================
        // Refresh
        // =====================================================================

        void Refresh()
        {
            if (_canvas == null || _level == null) return;
            _nameField.SetTextWithoutNotify(_level.Name);
            _capText.text = _level.Capacity.ToString();
            _colorsText.text = _level.ColorCount.ToString();
            _packText.text = _packLevel.ToString();
            _undoButton.interactable = _undo.Count > 0;
            _redoButton.interactable = _redo.Count > 0;
            RefreshTools();
            RefreshPalette();
            RebuildBoard();
            RefreshInfo();
        }

        void Info(string text, Color color)
        {
            if (_info == null) return;
            _info.text = text;
            _info.color = color;
        }

        void RefreshInfo()
        {
            if (_job != null) return;   // the running job owns the line
            if (!_level.Validate(out var error)) { Info(error, EditorUI.Bad); return; }
            int det = 0, luck = 0, part = 0, empty = 0;
            for (int i = 0; i < _level.TubeCount; i++)
            {
                switch (_level.PresetOf(i))
                {
                    case TubePreset.Deterministic: det++; break;
                    case TubePreset.Luck: luck++; break;
                    case TubePreset.PartialLuck: part++; break;
                    default: empty++; break;
                }
            }
            string s = $"OK: {_level.TubeCount} tubes, {_level.ColorCount} colours x {_level.Capacity}. Fixed {det}, luck {luck}, partial {part}, empty {empty}.";
            if (_level.Seeds != null) s += $" {_level.Seeds.SeedCount} seed(s) ready.";
            Info(s, EditorUI.Good);
        }

        void RefreshTools()
        {
            foreach (var kv in _toolImages) kv.Value.color = kv.Key == _tool ? EditorUI.Accent : EditorUI.ButtonColor;
        }

        void RefreshPalette()
        {
            int cap = _level.Capacity;
            foreach (var s in _swatches)
            {
                s.Bg.color = s.Value == _selected ? EditorUI.Accent : EditorUI.ButtonColor;
                if (s.Value > 0)
                {
                    int used = _level.UsageOf(s.Value);
                    bool inSet = s.Value <= _level.ColorCount;
                    s.Usage.text = inSet ? $"{used}/{cap}" : "";
                    s.Usage.color = used > cap ? EditorUI.Bad : used == cap ? EditorUI.Good : Color.white;
                    var c = s.IconColor;
                    s.Icon.color = inSet ? c : new Color(c.r, c.g, c.b, 0.3f);
                }
                else if (s.Value == EditorLevel.Random)
                {
                    int have = _level.RandomSlots, need = Mathf.Max(0, _level.RandomSlotsNeeded);
                    s.Usage.text = $"{have}/{need}";
                    s.Usage.color = have == need ? EditorUI.Good : have > need ? EditorUI.Bad : Color.white;
                }
            }
        }

        void RebuildBoard()
        {
            if (_board == null) return;
            for (int i = _board.childCount - 1; i >= 0; i--) Destroy(_board.GetChild(i).gameObject);
            var r = _board.rect;
            _boardSize = r.size;
            int n = _level.TubeCount, cap = _level.Capacity;
            if (n == 0)
            {
                EditorUI.Label(_board, "No tubes yet. Tap + Tube.", 44, TextAnchor.MiddleCenter, EditorUI.Muted);
                return;
            }
            if (r.width < 10f || r.height < 10f) return;

            int rows = Mathf.CeilToInt(n / 7f);
            int perRow = Mathf.CeilToInt(n / (float)rows);
            const float tagH = 46f;
            float colW = r.width / perRow, rowH = r.height / rows;
            float cell = Mathf.Min(colW * 0.8f / 1.12f, (rowH - tagH - 24f) / (cap + 0.3f), 130f);
            var tubeSize = new Vector2(cell * 1.12f, cell * (cap + 0.3f));

            for (int i = 0; i < n; i++)
            {
                int row = i / perRow, col = i % perRow;
                int inRow = Mathf.Min(perRow, n - row * perRow);
                float x = (col - (inRow - 1) / 2f) * colW;
                float y = r.height / 2f - rowH * (row + 0.5f) + tagH / 2f;
                BuildTube(i, new Vector2(x, y), tubeSize, cell);

                var tag = EditorUI.Rect("tag", _board);
                tag.anchoredPosition = new Vector2(x, y - tubeSize.y / 2f - tagH / 2f);
                tag.sizeDelta = new Vector2(colW, tagH);
                EditorUI.Label(tag, $"{i + 1} {PresetTag(_level.PresetOf(i))}", 28, TextAnchor.MiddleCenter, EditorUI.Muted, FontStyle.Normal);
            }
        }

        void BuildTube(int index, Vector2 pos, Vector2 size, float cell)
        {
            Color glass = index == _swapFrom ? EditorUI.Accent
                : _tool == EditorTool.Delete ? new Color(EditorUI.Danger.r, EditorUI.Danger.g, EditorUI.Danger.b, 0.35f)
                : new Color(1f, 1f, 1f, 0.12f);
            var tube = EditorUI.Image("tube " + (index + 1), _board, EditorUI.RoundRect, glass, true);
            tube.rectTransform.anchoredPosition = pos;
            tube.rectTransform.sizeDelta = size;
            var tb = tube.gameObject.AddComponent<Button>();
            tb.targetGraphic = tube;
            tb.onClick.AddListener(() => OnTubeClicked(index));

            var cells = _level.Tubes[index];
            for (int s = 0; s < _level.Capacity; s++)
            {
                int value = s < cells.Count ? cells[s] : 0;
                var ball = BallImage(tube.transform, value, cell * 0.9f);
                ball.rectTransform.anchoredPosition = new Vector2(0f, -size.y / 2f + cell * 0.15f + cell * (s + 0.5f));
                var cb = ball.gameObject.AddComponent<Button>();
                cb.targetGraphic = ball;
                int slot = s;
                cb.onClick.AddListener(() => OnCellClicked(index, slot));
            }
        }

        static string PresetTag(TubePreset preset) => preset switch
        {
            TubePreset.Deterministic => "fixed",
            TubePreset.Luck => "luck",
            TubePreset.PartialLuck => "partial",
            _ => "empty",
        };
    }
}
