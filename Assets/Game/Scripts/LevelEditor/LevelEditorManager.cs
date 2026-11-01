using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BallSortGame.Core;
using BallSortGame.Gameplay;
using BallSortGame.UI;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BallSortGame.LevelEditor
{
    /// <summary>
    /// In-app level editor (the ball-sort counterpart of Magic Sort's Modules.LevelEditor.WaterSort). It ships inside
    /// the normal game build: it attaches itself to any scene with a <see cref="GameManager"/> when <see cref="IsAllowed"/>
    /// (Unity Editor or a development build), and designers open it with F2 or a tap on the level pill. A release build
    /// never attaches it, so players never see it.
    ///
    /// The layout is shown on the real board (<see cref="GameManager.Preview"/>: the game's tubes, balls and background,
    /// grey "?" balls for luck slots) with the editor controls over it in the game's UI skin, the way the main menu
    /// sits over the board. Tap a ball to recolour it with the palette colour, tap the empty glass to fill the rest of
    /// the tube; TUBES / HEIGHT / COLOURS steppers size the level (a new tube comes as a full "?" tube of a new colour;
    /// erase it for a spare tube). Check fills the "?" cells with the colours of one solvable fill, so the designer sees
    /// the whole level; the "?" tool then hides a coloured ball in the game (tap again to show it): it keeps its colour,
    /// drawn with a "?" on it. The IN and OUT palette tools make a tube one-way
    /// (tap again to make it normal): in-only tubes start empty, out-only tubes start filled. The HIDE tool covers a tube
    /// until a neighbouring tube is completed (tap again to uncover it). The HEAVY tool puts the
    /// cannonball weight on any ball (tap again to take it off): it keeps its colour, but the player can only move it
    /// alone into an empty tube, where it sinks to the bottom. Test plays it, Check
    /// fills the "?" cells and rates the result, Save generates rated seeds of the wanted difficulty and writes them into the pack the game plays,
    /// replacing the slot the layout came from (or appending a new level). The LEVEL row's number box + GO jumps to a
    /// pack slot by its unique level number (the number it shows before the pack loops); a number past the end starts
    /// a new level that is appended on save.
    /// </summary>
    public partial class LevelEditorManager : MonoBehaviour
    {
        const int Eraser = 0;
        const int InOnlyTool = -2;      // palette tools that set the tube kind instead of painting
        const int OutOnlyTool = -3;
        const int HeavyTool = -4;       // toggles the weight on the tapped ball
        const int HiddenTool = -5;      // covers a tube until a neighbouring tube is completed
        const int MaxPaletteColors = 14;
        const int UndoDepth = 100;
        const int SeedsPerLevel = 5;          // Magic Sort generates five fills per level
        const float FreeScreenFraction = 0.235f;  // screen height kept clear of tubes at the top and the bottom for the controls

        /// <summary>Unity Editor or a development build (Build Profiles > Development Build). Never a release build.</summary>
        public static bool IsAllowed => Application.isEditor || Debug.isDebugBuild;
        public static string SaveDir => Path.Combine(Application.persistentDataPath, "LevelEditor");
        /// <summary>
        /// The pack the game plays (<see cref="GameManager.levelPackResource"/>): in the Unity Editor its JSON under
        /// Resources, so the saved levels are what the game loads next time; on a device a copy under persistentDataPath.
        /// </summary>
        public string PackPath
        {
            get
            {
                string res = string.IsNullOrEmpty(game?.levelPackResource) ? "data/editor_pack" : game.levelPackResource;
                return Application.isEditor
                    ? Path.Combine(Application.dataPath, "Game/Resources", res + ".json")
                    : Path.Combine(SaveDir, Path.GetFileName(res) + ".json");
            }
        }

        public GameManager game;
        public bool IsOpen => _open;
        public bool IsTesting => _testing;

        EditorLevel _level;
        int _selected = 1;              // palette value: colour > 0, EditorLevel.Random, or Eraser
        int _difficulty = 3;            // wanted bucket 1..7 for Save
        bool _open, _testing;
        readonly List<string> _undo = new List<string>();
        readonly List<string> _redo = new List<string>();

        // background job (solver / generator run off the main thread)
        Task _job;
        Action _jobDone;
        bool _generating;
        volatile int _progFills, _progSeeds;

        // UI
        Canvas _canvas;
        CanvasScaler _scaler;
        GameObject _panel, _testBar;
        Text _status, _tubesText, _capText, _colorsText, _diffText, _slotText;
        Button _undoButton;
        InputField _goField;
        float _typingUntil;             // keyboard shortcuts stay off for a moment after the GO box had the focus
        readonly List<Swatch> _swatches = new List<Swatch>();
        Canvas _hudCanvas;
        GraphicRaycaster _hudRaycaster;

        struct Swatch
        {
            public int Value;
            public GameObject Root;
            public Image Frame;
            public Text Usage;
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
            var game = FindFirstObjectByType<GameManager>();
            if (game == null) return;
            new GameObject("LevelEditor").AddComponent<LevelEditorManager>().game = game;
        }

        void Update()
        {
            PollJob();
            // keep the canvas at least 1080x2340 units in portrait and landscape windows
            if (_scaler != null) _scaler.matchWidthOrHeight = Screen.width * 2340f > Screen.height * 1080f ? 1f : 0f;

            if (_botting)
            {
                UpdateBot();
                return;
            }
            if (_testing)
            {
                if (Input.GetKeyDown(KeyCode.F2) || Input.GetKeyDown(KeyCode.Escape)) BackToEditor();
                return;
            }
            if (_open)
            {
                if (_botChoice != null && _botChoice.activeSelf)
                {
                    if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F2)) _botChoice.SetActive(false);
                    return;
                }
                if (_goField != null && _goField.isFocused) _typingUntil = Time.unscaledTime + 0.2f;
                if (Time.unscaledTime < _typingUntil) return;
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F2)) Close();
                else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    if (Input.GetKeyDown(KeyCode.Z)) Undo();
                    else if (Input.GetKeyDown(KeyCode.Y)) Redo();
                }
                else if (PointerInput.Down) TapBoard();
                return;
            }
            if (Input.GetKeyDown(KeyCode.F2)) { Open(); return; }
            DetectSecretTaps();
        }

        /// <summary>A tap on the level pill (top centre of the screen) opens the editor.</summary>
        void DetectSecretTaps()
        {
            if (!PointerInput.Down) return;
            Vector2 p = PointerInput.Position;
            if (p.y < Screen.height * 0.88f || Mathf.Abs(p.x - Screen.width * 0.5f) > Screen.width * 0.2f) return;
            Open();
        }

        // =====================================================================
        // Open / close / test
        // =====================================================================

        public void Open()
        {
            if (game == null) return;
            if (_canvas == null) BuildUI();
            if (_level == null) _level = StartingLayout();
            _open = true;
            _testing = false;
            ShowHud(false);
            _panel.SetActive(true);
            _testBar.SetActive(false);
            Canvas.ForceUpdateCanvases();   // a freshly built canvas has no size until it is laid out once
            RefreshPackLabel();
            Refresh();
        }

        public void Close()
        {
            if (_botting) StopBot(false);
            _open = _testing = false;
            _panel.SetActive(false);
            _testBar.SetActive(false);
            if (_botChoice != null) _botChoice.SetActive(false);
            game.MinCameraSize = 0f;
            bool moved = _level != null && InPack(_level.Slot) && SlotOf(game.CurrentLevel) != _level.Slot;
            bool overridden = game.OverrideLevel != null;
            if (overridden) game.SetOverrideLevel(null);   // back to the pack level that was being played
            if (moved) game.StartLevel(_level.Slot);       // GO went to another level: the game follows
            else if (!overridden) game.Restart();          // the preview replaced the board
            ShowHud(true);
            game.IsPaused = MainMenuView.IsOpen;
        }

        EditorLevel StartingLayout()
        {
            var cur = game.CurrentLevelData;
            int slot = game.OverrideLevel == null ? SlotOf(game.CurrentLevel) : 0;
            if (cur != null && cur.Tubes.Count > 0)
            {
                var lv = EditorLevel.FromLevelData(cur, slot > 0 ? $"lv{slot}" : NewName());
                lv.Slot = slot;
                return lv;
            }
            return EditorLevel.Standard(NewName(), 4, 2, 4);
        }

        // =====================================================================
        // Pack slots: the unique level number of a pack entry is its 1-based index (what it shows before the pack loops)
        // =====================================================================

        int PackCount => game.Stage?.Levels.Count ?? 0;

        bool InPack(int slot) => slot >= 1 && slot <= PackCount;

        /// <summary>The pack slot a level number plays (level 106 of a 100-level pack is slot 6); 0 without a pack.</summary>
        int SlotOf(int level)
        {
            int n = PackCount;
            return n == 0 ? 0 : ((level - 1) % n + n) % n + 1;
        }

        /// <summary>GO: the number typed in the LEVEL row.</summary>
        void Go()
        {
            string s = _goField != null ? _goField.text.Trim() : "";
            if (!int.TryParse(s, out int n) || n < 1) { Status("Type a level number (1 or more) and press GO.", EditorUI.Bad); return; }
            if (GoToLevel(n) && _goField != null) _goField.text = "";
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>
        /// Loads pack slot <paramref name="n"/> into the editor (Save then replaces it). A number past the end of the pack
        /// starts a new level with the current sizes, appended as the next slot on save.
        /// </summary>
        bool GoToLevel(int n)
        {
            if (_job != null) { Status("Still working on the last job...", EditorUI.Bad); return false; }
            int count = PackCount;
            if (n <= count)
            {
                EditorLevel lv;
                try { lv = EditorLevel.FromLevelData(game.Stage.Levels[n - 1], $"lv{n}"); }
                catch (Exception e) { Status($"Cannot load level {n} into the editor: {e.Message}", EditorUI.Bad); return false; }
                lv.Slot = n;
                Replace(lv);
                Status($"Level {n} of {count} loaded. Save replaces it in the pack.", EditorUI.Good);
                return true;
            }
            int slot = count + 1;
            int colors = Mathf.Clamp(_level != null ? _level.ColorCount : 4, 1, PaletteSize);
            int empties = _level != null ? Mathf.Max(2, _level.TubeCount - _level.ColorCount) : 2;
            var fresh = EditorLevel.Standard($"lv{slot}", colors, empties, _level != null ? _level.Capacity : 4);
            fresh.Slot = slot;
            Replace(fresh);
            Status(n == slot
                ? $"There is no level {n} yet: new level, saved as level {slot} of the pack."
                : $"The pack has {count} levels, so there is no level {n}: new level, saved as level {slot}.", Color.white);
            return true;
        }

        void RefreshSlotLabel()
        {
            if (_slotText == null || _level == null) return;
            int count = PackCount;
            _slotText.text = InPack(_level.Slot) ? $"LEVEL {_level.Slot} / {count}"
                : count > 0 ? $"NEW LEVEL {count + 1}"
                : "NO PACK LOADED";
        }

        /// <summary>Hides the game HUD while editing; its state (menu, popups) is kept.</summary>
        void ShowHud(bool on)
        {
            if (_hudCanvas == null)
            {
                var hud = FindFirstObjectByType<GameHUD>(FindObjectsInactive.Include);
                if (hud == null) return;
                _hudCanvas = hud.GetComponent<Canvas>();
                _hudRaycaster = hud.GetComponent<GraphicRaycaster>();
            }
            if (_hudCanvas != null) _hudCanvas.enabled = on;
            if (_hudRaycaster != null) _hudRaycaster.enabled = on;
        }

        /// <summary>
        /// What TEST and the bot's "this level" play: the generated seeds when there are any, otherwise one random
        /// fill of the layout. Null (with the reason in the status line) when the layout is not valid or cannot be filled.
        /// </summary>
        LevelData TestData()
        {
            var data = _level.Seeds;
            if (data != null) return data;
            if (!_level.Validate(out var error)) { Status(error, EditorUI.Bad); return null; }
            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            var board = _level.Fill(seed, out error);
            if (board == null) { Status(error, EditorUI.Bad); return null; }
            return new LevelData { PuzzleId = $"{_level.Name}_seed{seed}", Tubes = LevelFiller.ToTopFirst(board), TubeKinds = board.KindsOrNull(), Heavy = board.HeavyOrNull(), Hidden = _level.HiddenTopFirst() };
        }

        /// <summary>Plays the generated seeds when there are any, otherwise one random fill of the layout.</summary>
        void Test()
        {
            var data = TestData();
            if (data == null) return;
            _testing = true;
            _panel.SetActive(false);
            _testBar.SetActive(true);
            game.MinCameraSize = 0f;
            ShowHud(true);
            game.SetOverrideLevel(data);
            // the editor may have been opened from the main menu; the test is played, not menued
            var menu = FindFirstObjectByType<MainMenuView>();
            if (menu != null && MainMenuView.IsOpen) menu.Play();
        }

        void BackToEditor()
        {
            _testing = false;
            _testBar.SetActive(false);
            ShowHud(false);
            _panel.SetActive(true);
            Refresh();
        }

        // =====================================================================
        // Editing with undo
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
            int slot = _level.Slot;
            _level = JsonConvert.DeserializeObject<EditorLevel>(from[from.Count - 1]);
            if (_level.Slot == 0) _level.Slot = slot;   // a state from before the first save of a new level: it is still that level
            from.RemoveAt(from.Count - 1);
            Refresh();
        }

        void Replace(EditorLevel level) => Edit(() => { _level = level; return true; });

        static string NewName() => "custom_" + DateTime.Now.ToString("MMdd_HHmmss");

        int PaletteSize => Mathf.Clamp(game.ballSprites != null && game.ballSprites.Length > 0 ? game.ballSprites.Length : 12, 1, MaxPaletteColors);

        /// <summary>
        /// Tubes: a new tube is a full "?" tube of a new colour, so Save fills it and Reset keeps it (empty it with the
        /// eraser and lower COLOURS for a spare tube; once the palette is used up new tubes start empty). Removing a
        /// tube that had balls drops a colour again (never below the highest colour still placed).
        /// </summary>
        bool SetTubeCount(int count)
        {
            count = Mathf.Clamp(count, 1, BoardState.MaxTubes);
            if (count == _level.TubeCount) return false;
            while (_level.TubeCount < count)
            {
                _level.AddTube();
                if (_level.ColorCount < PaletteSize)
                {
                    _level.SetColorCount(_level.ColorCount + 1, PaletteSize);
                    _level.FillTube(_level.TubeCount - 1, EditorLevel.Random);
                }
            }
            while (_level.TubeCount > count)
            {
                int last = _level.TubeCount - 1;
                bool hadBalls = _level.Tubes[last].Count > 0;
                _level.RemoveTube(last);
                if (hadBalls) _level.SetColorCount(_level.ColorCount - 1, PaletteSize);
            }
            return true;
        }

        /// <summary>Reset: every colour a full "?" tube plus the current number of spare tubes (at least two).</summary>
        void Reset()
        {
            int colors = Mathf.Min(_level.ColorCount, PaletteSize);
            int empties = Mathf.Max(2, _level.TubeCount - colors);
            var lv = EditorLevel.Standard(_level.Name, colors, empties, _level.Capacity);
            lv.Slot = _level.Slot;
            Replace(lv);
        }

        // =====================================================================
        // Taps on the board (the real tubes, hit through their colliders)
        // =====================================================================

        void TapBoard()
        {
            if (GameManager.IsPointerOverUi()) return;
            if (game.cam == null) return;
            Vector3 wp = game.cam.ScreenToWorldPoint(PointerInput.Position);
            var hit = Physics2D.OverlapPoint(new Vector2(wp.x, wp.y));
            if (hit == null || !hit.TryGetComponent(out Tube tube)) return;
            int index = tube.Index;
            if (index < 0 || index >= _level.TubeCount) return;

            float localY = tube.transform.InverseTransformPoint(wp).y;
            int slot = Mathf.FloorToInt((localY - tube.FirstBallY + tube.BallPitch / 2f) / tube.BallPitch);
            int count = _level.Tubes[index].Count;
            if (slot < 0) slot = 0;
            if (_selected == InOnlyTool || _selected == OutOnlyTool || _selected == HiddenTool)
            {
                // kind tools: set the one-way / hidden rule, or back to normal when the tube already has it
                var kind = _selected == InOnlyTool ? TubeKind.InOnly : _selected == OutOnlyTool ? TubeKind.OutOnly : TubeKind.Hidden;
                if (_level.KindOf(index) == kind) kind = TubeKind.Normal;
                Edit(() => _level.SetKind(index, kind));
                return;
            }
            if (_selected == HeavyTool)
            {
                // heavy tool: the weight goes on (or off) the tapped ball, whatever its colour or place in the tube
                if (count == 0) { Status("Heavy: tap a ball to put the weight on it (tap again to take it off).", EditorUI.Bad); return; }
                int ball = Mathf.Min(slot, count - 1);   // the glass above the stack means the top ball
                Edit(() => _level.SetHeavy(index, ball, !_level.IsHeavy(index, ball)));
                return;
            }
            if (_level.KindOf(index) == TubeKind.InOnly && _selected != Eraser)
            {
                Status("In-only tubes start empty. Tap it with the IN tool to make it a normal tube again.", EditorUI.Bad);
                return;
            }
            if (slot < count && _selected == EditorLevel.Random && _level.Tubes[index][slot] > 0)
            {
                // "?" on a coloured ball: the game hides it (tap again to show it); the colour stays
                Edit(() => _level.SetBallHidden(index, slot, !_level.IsBallHidden(index, slot)));
            }
            else if (slot < count)
            {
                // a ball: recolour it, or take it out with the eraser (the balls above drop down)
                Edit(() => _level.Paint(index, slot, _selected));
            }
            else
            {
                // empty glass: fill the free slots with the value; the eraser empties the tube
                Edit(() =>
                {
                    if (_selected == Eraser) return _level.FillTube(index, Eraser);
                    bool any = false;
                    while (_level.Tubes[index].Count < _level.Capacity && _level.Paint(index, _level.Tubes[index].Count, _selected)) any = true;
                    return any;
                });
            }
        }

        void SelectValue(int value)
        {
            _selected = value;
            RefreshPalette();
        }

        // =====================================================================
        // Check / Save (generator = the local stand-in for Magic Sort's SolveLevelRequest backend)
        // =====================================================================

        /// <summary>
        /// Fills the "?" cells with the colours of one random fill, so the designer sees the whole level (hide chosen
        /// balls again with the "?" tool), then rates it: solvable, optimal-ish move count, and the bot difficulty bucket.
        /// </summary>
        void Check()
        {
            if (!_level.Validate(out var error)) { Status(error, EditorUI.Bad); return; }
            bool filled = _level.HasRandom;
            int seed = filled ? UnityEngine.Random.Range(1, int.MaxValue) : 0;
            var board = _level.Fill(seed, out error);
            if (board == null) { Status(error, EditorUI.Bad); return; }
            if (filled) Edit(() => _level.ApplyFill(board));
            string what = filled ? "Filled the ? balls" : "Layout";
            string hint = filled ? " Tap a ball with ? to hide it in the game." : "";
            LevelRating rating = null;
            RunJob("Checking with the solver and the playout bot...", () => rating = new LevelRater().Rate(board, RaterBudget.Default), () =>
            {
                if (rating.Verdict == SolveVerdict.Solvable)
                    Status($"{what}: solvable in {rating.OptimalMoves} moves, difficulty {rating.Bucket}/7.{hint}", EditorUI.Good);
                else
                    Status(rating.Verdict == SolveVerdict.Unsolvable ? $"{what}: unsolvable.{hint}" : $"{what}: the solver ran out of budget.{hint}", EditorUI.Bad);
            });
        }

        /// <summary>Generates the seeds when the layout changed since the last ones, then writes them into the pack.</summary>
        void Save()
        {
            if (_level.Seeds != null) { WritePack(); return; }
            if (!_level.Validate(out var error)) { Status(error, EditorUI.Bad); return; }
            var design = _level.ToDesign();
            var layout = LayoutKey();
            bool random = _level.HasRandom;
            int bucket = _difficulty;
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
                        WantedBuckets = Enumerable.Repeat(bucket, SeedsPerLevel).ToList(),
                        FallbackTolerance = 6,   // any solvable fill is better than none; the bucket is stored per seed
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
                if (LayoutKey() != layout) { Status("The layout changed while generating; save again.", EditorUI.Bad); return; }
                if (results.Count == 0) { Status(genError ?? "No solvable fill found.", EditorUI.Bad); return; }
                _level.Seeds = BuildSeeds(results, _level.HiddenTopFirst());
                WritePack();
            });
        }

        /// <summary>The seeds as pack data; <paramref name="hidden"/> marks the "?" cells, which the game keeps hidden.</summary>
        static LevelData BuildSeeds(List<GeneratedLevel> results, List<List<int>> hidden)
        {
            LevelData Make(GeneratedLevel g) => new LevelData { PuzzleId = g.Id, Tubes = g.TubesTopFirst, TubeKinds = g.TubeKinds, Heavy = g.Heavy, Steps = g.Steps, Difficulty = g.Bucket, Hidden = hidden };
            var main = Make(results[0]);
            if (results.Count > 1) main.Variants = results.Skip(1).Select(Make).ToList();
            return main;
        }

        /// <summary>
        /// Puts the seeds into the pack the game plays: the layout's slot is replaced, a new level (slot 0 or past the
        /// end) is appended and becomes that slot. The game's loaded pack is updated in place, so it plays the new
        /// level at once, and the JSON is written so it survives a restart.
        /// </summary>
        void WritePack()
        {
            var seeds = _level.Seeds;
            try
            {
                string path = PackPath;
                StageData stage = game.Stage;
                if (stage == null && File.Exists(path)) stage = JsonConvert.DeserializeObject<StageData>(File.ReadAllText(path));
                if (stage == null) stage = new StageData { StageId = Path.GetFileNameWithoutExtension(path) };
                int slot = _level.Slot;
                bool replaced = slot >= 1 && slot <= stage.Levels.Count;
                if (replaced) stage.Levels[slot - 1] = seeds;
                else { stage.Levels.Add(seeds); slot = stage.Levels.Count; _level.Slot = slot; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(stage));
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                string where = Application.isEditor ? game.levelPackResource : path;
                string buckets = string.Join(" ", Enumerable.Range(0, seeds.SeedCount).Select(i => seeds.SeedAt(i).Difficulty));
                RefreshPackLabel();
                RefreshSlotLabel();
                Status(replaced
                    ? $"Level {slot} replaced in {where} ({stage.Levels.Count} levels). Seed difficulty: {buckets}."
                    : $"Saved as new level {slot} of {where} ({stage.Levels.Count} levels). Seed difficulty: {buckets}.", EditorUI.Good);
            }
            catch (Exception e)
            {
                Status("Save failed: " + e.Message, EditorUI.Bad);
            }
        }

        void RunJob(string text, Action work, Action done)
        {
            if (_job != null) { Status("Still working on the last job...", EditorUI.Bad); return; }
            _jobDone = done;
            Status(text, Color.white);
            _job = Task.Run(work);
        }

        void PollJob()
        {
            if (_job == null) return;
            if (!_job.IsCompleted)
            {
                if (_generating && _status != null) _status.text = $"Generating... {_progFills} fills tried, {_progSeeds}/{SeedsPerLevel} seeds";
                return;
            }
            var job = _job;
            _job = null;
            if (job.IsFaulted)
            {
                _generating = false;
                var e = job.Exception?.GetBaseException();
                Debug.LogException(e);
                Status("Job failed: " + e?.Message, EditorUI.Bad);
                return;
            }
            _jobDone?.Invoke();
        }

        // =====================================================================
        // UI construction (game skin over the board, laid out like the HUD at 1080x2340)
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
            var skin = EditorUI.Skin;

            // ---- editor overlay: transparent, the board shows through
            var panel = EditorUI.Rect("Editor", root);
            EditorUI.Stretch(panel, Vector2.zero, Vector2.zero);
            SafeAreaFitter.Attach(panel);   // the rows hang from the panel's edges: keep them out of the cutout / gesture bar
            _panel = panel.gameObject;

            // top bar like the HUD: close left, title + status pill in the middle, TEST right
            var top = EditorUI.Row("top_bar", panel, true, 68f, 146f, 24f);
            EditorUI.IconButton(top, "btn - close", skin?.iconClose, "X", Close, 146f);
            var title = EditorUI.Image("title", top, EditorUI.PillSprite, EditorUI.Tint(skin?.pill, EditorUI.Bar), true);
            EditorUI.Flex(title, 1f);
            var titleText = EditorUI.Shadowed(EditorUI.Label(title.transform, "LEVEL EDITOR", 40, TextAnchor.UpperCenter, Color.white));
            titleText.rectTransform.offsetMin = new Vector2(20f, 80f);
            titleText.rectTransform.offsetMax = new Vector2(-20f, -14f);
            _status = EditorUI.Shadowed(EditorUI.Label(title.transform, "", 28, TextAnchor.UpperCenter, Color.white, FontStyle.Normal));
            _status.rectTransform.offsetMin = new Vector2(20f, 12f);
            _status.rectTransform.offsetMax = new Vector2(-20f, -66f);
            EditorUI.PillButton(top, "TEST", false, Test, 220f, 44);

            // size steppers
            var sizes = EditorUI.Row("sizes", panel, true, 230f, 100f, 12f);
            _tubesText = EditorUI.SkinStepper(sizes, "TUBES", d => Edit(() => SetTubeCount(_level.TubeCount + d)), 336f);
            _capText = EditorUI.SkinStepper(sizes, "HEIGHT", d => Edit(() => _level.SetCapacity(_level.Capacity + d)), 336f);
            _colorsText = EditorUI.SkinStepper(sizes, "COLOURS", d => Edit(() => _level.SetColorCount(_level.ColorCount + d, PaletteSize)), 336f);

            // level row: which pack slot is being edited, and a number box + GO to jump to a slot (or start a new level)
            var levelRow = EditorUI.Row("level", panel, true, 346f, 100f, 16f);
            var slotPill = EditorUI.Image("slot_label", levelRow, EditorUI.PillSprite, EditorUI.Tint(skin?.pill, EditorUI.Bar), true);
            EditorUI.Flex(slotPill, 1f);
            _slotText = EditorUI.Shadowed(EditorUI.Label(slotPill.transform, "", 32, TextAnchor.MiddleCenter, Color.white));
            _goField = EditorUI.Input(levelRow, "level no.", _ =>
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Go();
            });
            EditorUI.Fixed(_goField, 280f);
            _goField.contentType = InputField.ContentType.IntegerNumber;
            _goField.characterLimit = 6;
            _goField.textComponent.alignment = TextAnchor.MiddleCenter;
            ((Text)_goField.placeholder).alignment = TextAnchor.MiddleCenter;
            if (_goField.targetGraphic is Image goBg) { goBg.sprite = EditorUI.PillSprite; goBg.color = EditorUI.Tint(skin?.pill, EditorUI.Bar); }
            EditorUI.PillButton(levelRow, "GO", true, Go, 170f, 44);

            // pack row: which pack the game plays, and the bot that plays it through
            BuildPackRow(panel);

            // actions above the palette
            var actions = EditorUI.Row("actions", panel, false, 300f, 110f, 16f);
            _diffText = EditorUI.SkinStepper(actions, "DIFFICULTY", d => { _difficulty = Mathf.Clamp(_difficulty + d, 1, 7); _diffText.text = _difficulty.ToString(); }, 336f);
            _undoButton = EditorUI.IconButton(actions, "btn - undo", skin?.iconUndo, "Undo", Undo);
            EditorUI.IconButton(actions, "btn - reset", skin?.iconRestart, "Reset", Reset);
            EditorUI.IconButton(actions, "btn - check", skin?.iconCheck, "Check", Check);
            EditorUI.PillButton(actions, "SAVE", true, Save, 200f, 44);

            BuildPalette(panel);

            // ---- floating "back to the editor" pill shown while testing, above the booster bar
            var back = EditorUI.PillButton(root, "< EDITOR", false, BackToEditor, 360f, 44);
            var brt = (RectTransform)back.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 320f);
            brt.sizeDelta = new Vector2(360f, 100f);
            _testBar = back.gameObject;
            _testBar.SetActive(false);

            BuildBotUI(root);
        }

        /// <summary>Palette at the bottom: the colours in use, "?", the HEAVY, IN and OUT tools and the eraser; the selected one wears a frame.</summary>
        void BuildPalette(Transform parent)
        {
            var band = EditorUI.Band("palette", parent, false, 36f, 250f);
            var grid = band.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(96f, 112f);
            grid.spacing = new Vector2(8f, 8f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 10;   // 14 colours + ?, heavy, hidden, IN, OUT, eraser = 20 swatches in two rows
            grid.childAlignment = TextAnchor.MiddleCenter;

            var values = new List<int>();
            for (int c = 1; c <= PaletteSize; c++) values.Add(c);
            values.Add(EditorLevel.Random);
            values.Add(HeavyTool);
            values.Add(HiddenTool);
            values.Add(InOnlyTool);
            values.Add(OutOnlyTool);
            values.Add(Eraser);
            foreach (int v in values)
            {
                var cell = EditorUI.Rect("swatch " + v, band);
                var frame = EditorUI.Image("frame", cell, EditorUI.FrameSprite, EditorUI.Tint(EditorUI.Skin?.squareFrame, EditorUI.Accent), true);
                frame.rectTransform.anchoredPosition = new Vector2(0f, 10f);
                frame.rectTransform.sizeDelta = new Vector2(94f, 94f);
                frame.raycastTarget = false;
                var ball = BallImage(cell, v, 72f);
                ball.rectTransform.anchoredPosition = new Vector2(0f, 10f);
                var hit = cell.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                var b = cell.gameObject.AddComponent<Button>();
                b.targetGraphic = hit;
                b.transition = Selectable.Transition.None;
                int value = v;
                b.onClick.AddListener(() => SelectValue(value));
                var usageRt = EditorUI.Rect("usage", cell);
                usageRt.anchorMin = new Vector2(0f, 0f);
                usageRt.anchorMax = new Vector2(1f, 0f);
                usageRt.pivot = new Vector2(0.5f, 0f);
                usageRt.sizeDelta = new Vector2(0f, 30f);
                var usage = EditorUI.Shadowed(EditorUI.Label(usageRt, "", 24, TextAnchor.MiddleCenter, Color.white));
                _swatches.Add(new Swatch { Value = v, Root = cell.gameObject, Frame = frame, Usage = usage });
            }
        }

        Image BallImage(Transform parent, int value, float size)
        {
            var img = EditorUI.Image("ball", parent, EditorUI.Circle, Color.white);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.preserveAspect = true;
            img.raycastTarget = false;
            var skin = EditorUI.Skin;
            if (value == HeavyTool)
            {
                // a coloured ball wearing the cannonball weight, as on the board
                var sprite = game.BallSpriteFor(1);
                if (sprite != null) img.sprite = sprite;
                if (game.heavyBallSprite != null)
                {
                    var weight = EditorUI.Image("weight", img.transform, game.heavyBallSprite, Color.white);
                    weight.rectTransform.sizeDelta = new Vector2(size, size);
                    weight.preserveAspect = true;
                    weight.raycastTarget = false;
                }
                else EditorUI.Shadowed(EditorUI.Label(img.transform, "H", Mathf.RoundToInt(size * 0.6f), TextAnchor.MiddleCenter, Color.white));
            }
            else if (value == HiddenTool)
            {
                // the cover drawn over hidden tubes
                img.sprite = game.hiddenTubeSprite != null ? game.hiddenTubeSprite : game.tubeSprite;
                img.color = game.hiddenTubeSprite != null ? Color.white : new Color(0.25f, 0.55f, 0.42f, 1f);
                if (img.sprite == null) EditorUI.Shadowed(EditorUI.Label(img.transform, "#", Mathf.RoundToInt(size * 0.6f), TextAnchor.MiddleCenter, Color.white));
            }
            else if (value > 0)
            {
                var sprite = game.BallSpriteFor(value);
                if (sprite != null) img.sprite = sprite;
                else img.color = Color.HSVToRGB((value - 1) * 0.618f % 1f, 0.7f, 0.95f);
            }
            else if (value == EditorLevel.Random)
            {
                var sprite = game.BallSpriteFor(1);
                if (sprite != null) img.sprite = sprite;
                img.color = new Color(0.42f, 0.42f, 0.50f, 1f);
                EditorUI.Shadowed(EditorUI.Label(img.transform, "?", Mathf.RoundToInt(size * 0.6f), TextAnchor.MiddleCenter, Color.white));
            }
            else if (value == InOnlyTool || value == OutOnlyTool)
            {
                // the tube kind tools: the arrow drawn under one-way tubes, pointing down for IN and up for OUT
                img.sprite = EditorUI.SquareSprite;
                img.color = EditorUI.Tint(skin?.buttonSquare, EditorUI.ButtonColor);
                img.type = Image.Type.Sliced;
                if (skin != null && skin.iconArrow != null)
                {
                    var arrow = EditorUI.Image("icon", img.transform, skin.iconArrow, Color.white);
                    arrow.rectTransform.sizeDelta = new Vector2(size * 0.6f, size * 0.6f);
                    arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, value == InOnlyTool ? 180f : 0f);
                    arrow.preserveAspect = true;
                    arrow.raycastTarget = false;
                }
                else EditorUI.Shadowed(EditorUI.Label(img.transform, value == InOnlyTool ? "v" : "^", Mathf.RoundToInt(size * 0.6f), TextAnchor.MiddleCenter, Color.white));
            }
            else
            {
                img.sprite = EditorUI.CircleSprite;
                img.color = EditorUI.Tint(skin?.buttonCircle, EditorUI.Danger);
                if (skin != null && skin.iconClose != null)
                {
                    var x = EditorUI.Image("icon", img.transform, skin.iconClose, Color.white);
                    x.rectTransform.sizeDelta = new Vector2(size * 0.55f, size * 0.55f);
                    x.preserveAspect = true;
                    x.raycastTarget = false;
                }
                else EditorUI.Shadowed(EditorUI.Label(img.transform, "X", Mathf.RoundToInt(size * 0.5f), TextAnchor.MiddleCenter, Color.white));
            }
            return img;
        }

        // =====================================================================
        // Refresh
        // =====================================================================

        void Refresh()
        {
            if (_canvas == null || _level == null) return;
            _tubesText.text = _level.TubeCount.ToString();
            _capText.text = _level.Capacity.ToString();
            _colorsText.text = _level.ColorCount.ToString();
            _diffText.text = _difficulty.ToString();
            _undoButton.interactable = _undo.Count > 0;
            if (_selected > _level.ColorCount) _selected = 1;
            RefreshSlotLabel();
            RefreshPalette();
            ShowBoard();
            RefreshStatus();
        }

        /// <summary>Puts the layout on the real board, zoomed out enough to keep the controls off the tubes.</summary>
        void ShowBoard()
        {
            int n = _level.TubeCount;
            float minSize = 0f;
            if (n > 0)
            {
                int rows = Mathf.CeilToInt(n / (float)Mathf.Max(1, game.MaxTubesPerRow));
                float top = (rows - 1) / 2f * game.RowGap + game.TubeHeight / 2f + 0.3f;
                minSize = top / Mathf.Max(0.2f, 1f - 2f * FreeScreenFraction - Mathf.Abs(game.BoardOffsetFactor));
            }
            game.MinCameraSize = minSize;
            game.Preview(_level.Tubes, _level.Capacity, _level.Kinds, _level.HeavyFlags, _level.HiddenBalls);
        }

        void Status(string text, Color color)
        {
            if (_status == null) return;
            _status.text = text;
            _status.color = color;
        }

        void RefreshStatus()
        {
            if (_job != null) return;   // the running job owns the line
            if (!_level.Validate(out var error)) { Status(error, EditorUI.Bad); return; }
            string s = $"{_level.TubeCount} tubes, {_level.ColorCount} colours x {_level.Capacity}";
            if (_level.HasHiddenBalls) s += $", {_level.HiddenBallCount} hidden balls";
            if (_level.HasHeavy) s += $", {_level.HeavyCount} heavy";
            int inOnly = _level.CountKind(TubeKind.InOnly), outOnly = _level.CountKind(TubeKind.OutOnly);
            if (inOnly > 0) s += $", {inOnly} in-only";
            if (outOnly > 0) s += $", {outOnly} out-only";
            int hiddenTubes = _level.CountKind(TubeKind.Hidden);
            if (hiddenTubes > 0) s += $", {hiddenTubes} hidden tube(s)";
            s += _level.Seeds != null ? $". {_level.Seeds.SeedCount} seed(s) ready." : ". Ready to test or save.";
            Status(s, EditorUI.Good);
        }

        void RefreshPalette()
        {
            int cap = _level.Capacity;
            foreach (var s in _swatches)
            {
                bool shown = s.Value <= _level.ColorCount;   // colours beyond the COLOURS count stay hidden; ?, tools and eraser are <= 0
                s.Root.SetActive(shown);
                if (!shown) continue;
                s.Frame.enabled = s.Value == _selected;
                if (s.Value == HeavyTool)
                {
                    int n = _level.HeavyCount;
                    s.Usage.text = n > 0 ? $"HEAVY {n}" : "HEAVY";
                    s.Usage.color = Color.white;
                }
                else if (s.Value > 0)
                {
                    int used = _level.UsageOf(s.Value);
                    s.Usage.text = $"{used}/{cap}";
                    s.Usage.color = used > cap ? EditorUI.Bad : used == cap ? EditorUI.Good : Color.white;
                }
                else if (s.Value == EditorLevel.Random)
                {
                    int have = _level.RandomSlots, need = Mathf.Max(0, _level.RandomSlotsNeeded);
                    s.Usage.text = $"{have}/{need}";
                    s.Usage.color = have == need ? EditorUI.Good : have > need ? EditorUI.Bad : Color.white;
                }
                else if (s.Value == HiddenTool)
                {
                    int n = _level.CountKind(TubeKind.Hidden);
                    s.Usage.text = n > 0 ? $"HIDE {n}" : "HIDE";
                    s.Usage.color = Color.white;
                }
                else if (s.Value == InOnlyTool || s.Value == OutOnlyTool)
                {
                    int n = _level.CountKind(s.Value == InOnlyTool ? TubeKind.InOnly : TubeKind.OutOnly);
                    s.Usage.text = (s.Value == InOnlyTool ? "IN" : "OUT") + (n > 0 ? $" {n}" : "");
                    s.Usage.color = Color.white;
                }
                else s.Usage.text = "";
            }
        }
    }
}
