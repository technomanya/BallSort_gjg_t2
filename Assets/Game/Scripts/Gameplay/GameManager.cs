using System;
using System.Collections;
using System.Collections.Generic;
using BallSortGame.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BallSortGame.Gameplay
{
    /// <summary>
    /// Runtime game loop, modelled on BallSort.GameMgr of the reference game:
    /// StartGame -> build tubes (ComputeTubePos / TubeLayout) -> OnTubeClick (select / move) ->
    /// OnMoveEnd (tube-full check, win check) -> EndGame; Undo with a RecordStep stack; AddTube; CheckNoMove via the solver.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public struct RecordStep
        {
            public int From, To, Count;
        }

        [Header("Prefabs")]
        public Tube tubePrefab;
        public Ball ballPrefab;

        [Header("Skin")]
        public Sprite tubeSprite;
        [Tooltip("Glass of the little tube the little-tube booster adds (Art/Tubes/Tube short); its height comes from the sprite.")]
        public Sprite smallTubeSprite;
        [Tooltip("Moves the ball in the little tube up (+) or down (-) from where the big tubes hold their bottom ball.")]
        public float SmallTubeBallOffset = 0f;
        [Tooltip("Arrow (pointing up) shown under one-way tubes; UISkin.iconArrow is used when empty.")]
        public Sprite arrowSprite;
        [Tooltip("Cover drawn over a hidden tube (Art/Tubes/Hidden tube) until a neighbouring tube is completed; the glass tinted green when empty.")]
        public Sprite hiddenTubeSprite;
        [Tooltip("Color index 1 uses element 0, index 2 element 1, ...")]
        public Sprite[] ballSprites;
        [Tooltip("Weight drawn over a heavy ball (Art/Balls/NewBalls/Cannonball): a heavy ball sits at the bottom of its tube and only moves into an empty tube.")]
        public Sprite heavyBallSprite;
        public float TubeWidth = 1.81f;
        public float TubeHeight = 4.8f;
        public float TubeSpace = 0.42f;
        public float FirstBallY = -1.79f;
        public float BallPitch = 1.08f;
        public float LiftY = 3.05f;
        [Tooltip("Rows with fewer tubes than this keep the width of an AvgSpaceCount row and spread evenly (reference TubeLayout).")]
        public int AvgSpaceCount = 5;
        public int MaxTubesPerRow = 7;
        public float RowGap = 6.3f;

        [Header("Motion")]
        public float BallMoveSpeed = 26f;
        public float PerBallDelay = 0.045f;
        public float WinDelay = 0.6f;

        [Header("Level")]
        public string levelPackResource = "data/levels";
        public int MaxAddTube = 1;
        [Tooltip("Little tubes per level: one ball each, a parking spot that never counts as a finished tube.")]
        public int MaxAddSmallTube = 1;
        public int UndoLimit = 5;

        [Header("Editor Test")]
        [Tooltip("Unity Editor only: start at Test Level instead of the saved level. Wins are not saved while this is on.")]
        public bool useTestLevel;
        [Min(1)] public int testLevel = 1;

        [Header("Scene")]
        public Camera cam;
        public Transform tubesRoot;
        [Tooltip("Extra world units kept free around the widest row when fitting the camera.")]
        public float HorizontalMargin = 0.35f;
        [Tooltip("Fraction of the camera's vertical half-size the board is shifted down to leave room for the top bar.")]
        public float BoardOffsetFactor = -0.05f;
        [Tooltip("Screen height fraction covered by the HUD top bar (down to the LEVEL pill's bottom edge: 301 of 2340 px in the reference layout); flight arcs stay below it. The HUD measures the real value at runtime (SetHudBands).")]
        public float TopBarScreenFraction = 0.129f;
        [Tooltip("Screen height fraction covered by the HUD booster bar (300 of 2340 px in the reference layout). The HUD measures the real value at runtime (SetHudBands).")]
        public float BottomBarScreenFraction = 0.128f;
        [Tooltip("Extra world units kept between the highest point of a flight arc and whatever is above it.")]
        public float ArcSafetyMargin = 0.15f;
        /// <summary>Lower bound for the fitted camera size; the level editor raises it so its controls stay clear of the board. Not saved.</summary>
        [NonSerialized] public float MinCameraSize;

        // ---------- events for the HUD ----------
        public event Action<int, LevelData> LevelStarted;
        public event Action<int, int> MovesChanged;   // (moves, undosLeft)
        public event Action<int> AddTubeChanged;      // add tubes left
        public event Action<int> SmallTubeChanged;    // little tubes left
        public event Action<int> LevelWon;
        public event Action<Tube> TubeCompleted;      // a move left the tube full of one colour
        public event Action<Tube> HiddenTubeOpened;   // a completed neighbour opened a hidden tube
        public event Action NoMoves;
        public event Action<string> Toast;

        const string SavedLevelKey = "ballsort.level";

        /// <summary>Level the game opens at: one past the highest level won, 1 for a new player. Kept in PlayerPrefs.</summary>
        public static int SavedLevel => Mathf.Max(1, PlayerPrefs.GetInt(SavedLevelKey, 1));

        public static void ClearSavedLevel()
        {
            PlayerPrefs.DeleteKey(SavedLevelKey);
            PlayerPrefs.Save();
        }

        /// <summary>True while the Editor Test level is in use; progress is not saved then.</summary>
        public bool IsTestLevelOn => Application.isEditor && useTestLevel;

        public int CurrentLevel { get; private set; }
        public LevelData CurrentLevelData { get; private set; }
        /// <summary>Tries of <see cref="CurrentLevel"/> before this one: 0 on the first try, +1 per restart or retry after a dead end.</summary>
        public int CurrentAttempt { get; private set; }
        /// <summary>A win on the first try (see <see cref="CurrentAttempt"/>) in a real play, not a test or a bot run.</summary>
        public bool IsOneShotPlay => CurrentAttempt == 0 && !IsTestLevelOn && OverrideLevel == null && !IsBotPlaying;
        /// <summary>Loaded level pack (null when it failed to load).</summary>
        public StageData Stage => _stage;
        /// <summary>Level played instead of the pack slot (set by the in-app level editor's Test); null = play the pack.</summary>
        public LevelData OverrideLevel { get; private set; }
        public IReadOnlyList<Tube> Tubes => _tubes;
        public bool IsPaused { get => _isPause; set { _isPause = value; if (value) ClearSelection(); } }
        public int UndosLeft => Mathf.Max(0, UndoLimit - _undosUsed);
        public int AddTubesLeft => Mathf.Max(0, MaxAddTube - _addedTubes);
        public int SmallTubesLeft => Mathf.Max(0, MaxAddSmallTube - _addedSmallTubes);
        /// <summary>True while the level bot drives the board: taps are ignored and wins do not save progress.</summary>
        public bool IsBotPlaying { get; set; }
        /// <summary>Capacity of the tubes on the board.</summary>
        public int Capacity => _capacity;

        readonly List<Tube> _tubes = new List<Tube>();
        readonly Stack<RecordStep> _record = new Stack<RecordStep>();
        readonly BallSortSolver _solver = new BallSortSolver();
        readonly Dictionary<int, int> _attemptByLevel = new Dictionary<int, int>();
        readonly Dictionary<int, int> _lastSeedByLevel = new Dictionary<int, int>();
        readonly System.Random _seedRng = new System.Random();

        StageData _stage;
        Tube _curTube;
        bool _isPause;
        bool _gameOver;
        int _sumStep;
        int _undosUsed;
        int _addedTubes;
        int _addedSmallTubes;
        int _capacity;
        Coroutine _noMoveCheck;

        void Awake()
        {
            Time.timeScale = 1f; // the Editor keeps a debug time scale across play sessions; the game always starts at normal speed
            if (cam == null) cam = Camera.main;
            if (tubesRoot == null)
            {
                var go = new GameObject("Tubes");
                go.transform.SetParent(transform, false);
                tubesRoot = go.transform;
            }
        }

        void Start()
        {
            _stage = LevelLoader.LoadStage(levelPackResource);
            if (_stage == null || _stage.Levels.Count == 0)
            {
                Debug.LogError("GameManager: no levels loaded.");
                return;
            }
            StartLevel(IsTestLevelOn ? testLevel : SavedLevel);
        }

        void Update()
        {
            if (_isPause || _gameOver || IsBotPlaying) return;
            if (!PointerInput.Down) return;
            if (IsPointerOverUi()) return;

            Vector3 wp = cam.ScreenToWorldPoint(PointerInput.Position);
            var hit = Physics2D.OverlapPoint(new Vector2(wp.x, wp.y));
            if (hit == null) return;
            var tube = hit.GetComponent<Tube>();
            if (tube != null) OnTubeClick(tube);
        }

        /// <summary>True when the tap that is being handled landed on UI (see <see cref="PointerInput.IsOverUi"/>).</summary>
        public static bool IsPointerOverUi() => PointerInput.IsOverUi();

        // =====================================================================
        // Level lifecycle
        // =====================================================================

        public void StartLevel(int level)
        {
            if (OverrideLevel == null && (_stage == null || _stage.Levels.Count == 0)) return;
            CurrentLevel = level;
            // Generated packs carry several fills ("seeds") per level: attempt k plays seed k, then a random
            // one that is not the last played; easier dlb seeds take over after the cap of failed attempts.
            var slot = OverrideLevel;
            if (slot == null)
            {
                int idx = ((level - 1) % _stage.Levels.Count + _stage.Levels.Count) % _stage.Levels.Count;
                slot = _stage.Levels[idx];
            }
            _attemptByLevel.TryGetValue(level, out int attempt);
            _lastSeedByLevel.TryGetValue(level, out int lastSeed);
            CurrentLevelData = slot.ForAttempt(attempt, attempt == 0 ? -1 : lastSeed, _seedRng, out int seedIndex);
            CurrentAttempt = attempt;
            _attemptByLevel[level] = attempt + 1;
            _lastSeedByLevel[level] = seedIndex;
            Begin(CurrentLevelData);
        }

        /// <summary>
        /// Plays one specific fill of a pack level: <paramref name="seedIndex"/> 0 is the main seed, 1.. the variants,
        /// 1000+ the dlb fills (see <see cref="LevelData.ForAttempt"/>). Ignores the editor override level and leaves the
        /// retry counters alone. Used by the level bot.
        /// </summary>
        public void StartLevel(int level, int seedIndex)
        {
            if (_stage == null || _stage.Levels.Count == 0) return;
            CurrentLevel = level;
            CurrentAttempt = 0;
            int idx = ((level - 1) % _stage.Levels.Count + _stage.Levels.Count) % _stage.Levels.Count;
            var slot = _stage.Levels[idx];
            CurrentLevelData = seedIndex >= 1000 && slot.DlbVariants != null && slot.DlbVariants.Count > 0
                ? slot.DlbVariants[Mathf.Clamp(seedIndex - 1000, 0, slot.DlbVariants.Count - 1)]
                : slot.SeedAt(seedIndex);
            Begin(CurrentLevelData);
        }

        void Begin(LevelData data)
        {
            StartGameInternal(data);
            LevelStarted?.Invoke(CurrentLevel, data);
            MovesChanged?.Invoke(_sumStep, UndosLeft);
            AddTubeChanged?.Invoke(AddTubesLeft);
            SmallTubeChanged?.Invoke(SmallTubesLeft);
        }

        /// <summary>
        /// Plays <paramref name="level"/> (with its variants) in place of the current pack slot, starting at seed 0;
        /// null goes back to the pack. Used by the in-app level editor.
        /// </summary>
        public void SetOverrideLevel(LevelData level)
        {
            OverrideLevel = level;
            int lv = Mathf.Max(1, CurrentLevel);
            _attemptByLevel.Remove(lv);
            _lastSeedByLevel.Remove(lv);
            StartLevel(lv);
        }

        /// <summary>
        /// Level editor: shows a layout on the real board without starting a game. Tubes are bottom-first; a cell
        /// below 1 is an unknown ("?") slot the generator fills later. The board stays paused until
        /// <see cref="StartLevel"/> or <see cref="SetOverrideLevel"/> brings the game back.
        /// </summary>
        public void Preview(List<List<int>> tubesBottomFirst, int capacity) => Preview(tubesBottomFirst, capacity, null, null);

        /// <inheritdoc cref="Preview(List{List{int}}, int)"/>
        /// <param name="kinds">Per-tube <see cref="TubeKind"/> as ints (null = all normal).</param>
        /// <param name="heavy">Per-tube 0/1: the bottom ball is heavy (null = none).</param>
        public void Preview(List<List<int>> tubesBottomFirst, int capacity, IList<int> kinds, IList<int> heavy)
        {
            if (_noMoveCheck != null) { StopCoroutine(_noMoveCheck); _noMoveCheck = null; }
            StopAllCoroutines();
            ClearTubes();
            _record.Clear();
            _curTube = null;
            _gameOver = false;
            _isPause = true;
            _capacity = Mathf.Max(1, capacity);
            for (int i = 0; i < tubesBottomFirst.Count; i++)
            {
                var kind = kinds != null && i < kinds.Count ? (TubeKind)kinds[i] : TubeKind.Normal;
                var tube = CreateTube(i, kind);
                tube.SetBalls(tubesBottomFirst[i], v => v > 0 ? CreateBall(v) : CreateUnknownBall());
                if (heavy != null && i < heavy.Count && heavy[i] != 0 && tube.Count > 0) tube.Balls[0].SetHeavy(true, heavyBallSprite);
                if (tube.IsHiddenTube) tube.SetCoverAlpha(0.55f);   // see-through: the designer paints the balls under it
            }
            ComputeTubePos(animate: false);
        }

        /// <summary>Grey ball with a "?" for the editor's hidden ("?") cells in the preview.</summary>
        Ball CreateUnknownBall()
        {
            Ball ball = ballPrefab != null
                ? Instantiate(ballPrefab)
                : new GameObject("Ball", typeof(SpriteRenderer)).AddComponent<Ball>();
            ball.Setup(-1, HiddenBallSprite, 2);
            ball.name = "Ball_unknown";
            ball.Hide(HiddenBallSprite);
            return ball;
        }

        public void Restart() => StartLevel(CurrentLevel);
        public void NextLevel() => StartLevel(CurrentLevel + 1);

        /// <summary>Editor test: plays <see cref="testLevel"/> now and turns Test Level on, so wins are not saved.</summary>
        public void PlayTestLevel()
        {
            useTestLevel = true;
            StartLevel(testLevel);
        }

        void StartGameInternal(LevelData level)
        {
            if (_noMoveCheck != null) { StopCoroutine(_noMoveCheck); _noMoveCheck = null; }
            StopAllCoroutines();
            ClearTubes();
            _record.Clear();
            _sumStep = 0;
            _undosUsed = 0;
            _addedTubes = 0;
            _addedSmallTubes = 0;
            _curTube = null;
            _gameOver = false;
            _isPause = false;
            _capacity = Mathf.Max(1, level.Capacity);

            for (int i = 0; i < level.Tubes.Count; i++)
            {
                var tube = CreateTube(i, level.KindOf(i));
                // level tubes are listed top-first; the Tube wants bottom-first colors
                var topFirst = level.Tubes[i];
                var bottomFirst = new List<int>(topFirst.Count);
                var hiddenBottomFirst = new List<bool>(topFirst.Count);
                for (int j = topFirst.Count - 1; j >= 0; j--)
                {
                    if (topFirst[j] <= 0) continue;
                    bottomFirst.Add(topFirst[j]);
                    hiddenBottomFirst.Add(level.IsHidden(i, j));
                }
                tube.SetBalls(bottomFirst, CreateBall);
                // hidden balls (the editor's "?" cells): grey until they become the top ball of their tube
                for (int k = 0; k < hiddenBottomFirst.Count; k++)
                    if (hiddenBottomFirst[k]) tube.Balls[k].Hide(HiddenBallSprite);
                // heavy ball: the bottom ball wears the weight and only ever moves into an empty tube
                if (level.IsHeavyBottom(i) && tube.Count > 0) tube.Balls[0].SetHeavy(true, heavyBallSprite);
                tube.RevealTop();
                if (tube.IsCompleted) tube.RevealAll();
            }
            OpenHiddenTubes(animate: false);   // a tube completed from the start opens its neighbours at once
            ComputeTubePos(animate: false);
        }

        /// <summary>Sprite shown for a hidden ball (tinted grey with a "?" by <see cref="Ball.Hide"/>).</summary>
        Sprite HiddenBallSprite => BallSpriteFor(1);

        Sprite ArrowSprite => arrowSprite != null ? arrowSprite : UI.UISkin.Current != null ? UI.UISkin.Current.iconArrow : null;

        Tube CreateTube(int index, TubeKind kind = TubeKind.Normal)
        {
            var tube = NewTube();
            tube.Setup(index, _capacity, tubeSprite, 3);
            tube.SetKind(kind, ArrowSprite, hiddenTubeSprite);
            _tubes.Add(tube);
            return tube;
        }

        /// <summary>
        /// The little tube: one ball, short glass standing on the row's floor (see <see cref="ComputeTubePos"/>), with
        /// lifted balls rising as high as in the big tubes so flights look the same.
        /// </summary>
        Tube CreateSmallTube(int index)
        {
            var tube = NewTube();
            float h = smallTubeSprite != null ? smallTubeSprite.bounds.size.y : TubeHeight;
            float drop = (TubeHeight - h) / 2f;   // how much lower its centre sits than a big tube's
            tube.TubeHeight = h;
            tube.FirstBallY = FirstBallY + drop + SmallTubeBallOffset;
            tube.LiftY = LiftY + drop;
            tube.Setup(index, 1, smallTubeSprite != null ? smallTubeSprite : tubeSprite, 3);
            tube.SetKind(TubeKind.Spare, null);
            tube.name = $"SmallTube_{index}";
            _tubes.Add(tube);
            return tube;
        }

        Tube NewTube()
        {
            Tube tube = tubePrefab != null
                ? Instantiate(tubePrefab, tubesRoot)
                : new GameObject("Tube", typeof(SpriteRenderer), typeof(BoxCollider2D)).AddComponent<Tube>();
            if (tubePrefab == null) tube.transform.SetParent(tubesRoot, false);
            tube.TubeWidth = TubeWidth;
            tube.TubeHeight = TubeHeight;
            tube.FirstBallY = FirstBallY;
            tube.BallPitch = BallPitch;
            tube.LiftY = LiftY;
            return tube;
        }

        Ball CreateBall(int colorIndex)
        {
            Ball ball = ballPrefab != null
                ? Instantiate(ballPrefab)
                : new GameObject("Ball", typeof(SpriteRenderer)).AddComponent<Ball>();
            ball.Setup(colorIndex, BallSpriteFor(colorIndex), 2);
            return ball;
        }

        public Sprite BallSpriteFor(int colorIndex)
        {
            if (ballSprites == null || ballSprites.Length == 0) return null;
            int i = (colorIndex - 1) % ballSprites.Length;
            if (i < 0) i += ballSprites.Length;
            return ballSprites[i];
        }

        void ClearTubes()
        {
            foreach (var t in _tubes) if (t != null) Destroy(t.gameObject);
            _tubes.Clear();
        }

        // =====================================================================
        // Layout (reference GameMgr.TubeLayout / ComputeTubePos / CalculateTubeLayoutCameraInfo)
        // =====================================================================

        /// <summary>One row of x positions. Returns the row width.</summary>
        public float TubeLayout(int count, bool judgeSpecialRow, List<float> xs)
        {
            xs.Clear();
            float pitch = TubeSpace + TubeWidth;
            int avg = AvgSpaceCount + (judgeSpecialRow ? 1 : 0);
            float rowWidth;
            if (count < avg)
            {
                rowWidth = TubeSpace + pitch * avg;
                pitch = TubeWidth + (rowWidth - TubeWidth * count) / (count + 1);
            }
            else
            {
                rowWidth = TubeSpace + pitch * count;
            }
            float x = pitch - rowWidth / 2f - TubeWidth / 2f;
            for (int i = 0; i < count; i++) { xs.Add(x); x += pitch; }
            return rowWidth;
        }

        /// <summary>Share of the screen height between the HUD bars in the 1080x2340 reference layout.</summary>
        const float ReferenceHudBand = 1f - 0.129f - 0.128f;

        /// <summary>
        /// Called by the HUD with the measured share of the screen height its top bar (down to the LEVEL pill) and its
        /// booster bar take, so the camera fit and the flight arcs follow the real layout on every screen (safe area,
        /// 16:9 phones, tablets). Re-lays the board out when the values changed.
        /// </summary>
        public void SetHudBands(float top, float bottom)
        {
            if (Mathf.Abs(top - TopBarScreenFraction) < 0.002f && Mathf.Abs(bottom - BottomBarScreenFraction) < 0.002f) return;
            TopBarScreenFraction = top;
            BottomBarScreenFraction = bottom;
            ComputeTubePos(animate: false);
        }

        void ComputeTubePos(bool animate)
        {
            int n = _tubes.Count;
            if (n == 0) return;
            int rows = Mathf.CeilToInt(n / (float)MaxTubesPerRow);
            int perRow = Mathf.CeilToInt(n / (float)rows);

            var xs = new List<float>();
            float maxRowWidth = 0f;
            var positions = new Vector2[n];
            var rowOf = new int[n];
            var rowY = new float[rows];
            int idx = 0;
            for (int r = 0; r < rows; r++)
            {
                int countInRow = Mathf.Min(perRow, n - idx);
                bool special = rows > 1 && r == rows - 1 && countInRow < perRow;
                float w = TubeLayout(countInRow, special, xs);
                maxRowWidth = Mathf.Max(maxRowWidth, w);
                float y = ((rows - 1) / 2f - r) * RowGap;
                rowY[r] = y;
                for (int i = 0; i < countInRow; i++) { rowOf[idx] = r; positions[idx++] = new Vector2(xs[i], y); }
            }

            // camera: fit the widest row horizontally and all rows vertically. The rows must fit the band of the screen
            // between the HUD bars: the 2.6 margin suits the 1080x2340 reference, and when the bars take a bigger share of
            // a shorter screen (16:9 phones, tablets) the camera zooms out by the same ratio.
            float aspect = cam != null ? cam.aspect : 9f / 16f;
            float band = Mathf.Max(0.3f, 1f - TopBarScreenFraction - BottomBarScreenFraction);
            float bandScale = Mathf.Max(1f, ReferenceHudBand / band);
            float size = 5f;
            size = Mathf.Max(size, (maxRowWidth / 2f + HorizontalMargin) / aspect);
            size = Mathf.Max(size, (rows * RowGap / 2f + 2.6f) * bandScale);
            size = Mathf.Max(size, MinCameraSize);
            if (cam != null)
            {
                cam.orthographic = true;
                cam.orthographicSize = size;
            }
            // the board sits in the middle of the band between the bars (plus the tuned offset)
            float boardY = size * (BoardOffsetFactor + BottomBarScreenFraction - TopBarScreenFraction);
            tubesRoot.position = new Vector3(0f, boardY, 0f);

            // free space above each row: up to the top bar for the first row, up to the row above otherwise.
            // Balls use it for an elliptic flight arc when there is enough of it.
            float camTop = (cam != null ? cam.transform.position.y : 0f) + size;
            float topBarWorld = 2f * size * TopBarScreenFraction;
            for (int i = 0; i < n; i++)
            {
                // a short (little) tube stands on the same floor as the big ones
                float drop = _tubes[i].IsSpare ? (_tubes[i].TubeHeight - TubeHeight) / 2f : 0f;
                _tubes[i].transform.localPosition = new Vector3(positions[i].x, positions[i].y + drop, 0f);
                _tubes[i].SnapBalls();
                int r = rowOf[i];
                float liftTop = boardY + positions[i].y + LiftY + 0.5f;
                float ceiling = r == 0 ? camTop - topBarWorld : boardY + rowY[r - 1] - TubeHeight / 2f;
                _tubes[i].ArcHeadroom = Mathf.Max(0f, ceiling - liftTop - ArcSafetyMargin);
            }
        }

        // =====================================================================
        // Input flow (reference GameMgr.OnTubeClick)
        // =====================================================================

        public void OnTubeClick(Tube tube)
        {
            if (_isPause || _gameOver || tube == null) return;

            // a tube that is still animating cannot be used; clicking it cancels the current selection
            if (tube.IsMoving)
            {
                ClearSelection();
                return;
            }

            if (_curTube == null)
            {
                if (tube.IsHiddenTube) { tube.Shake(); return; }   // covered until a neighbour is completed
                if (tube.IsEmpty) return;
                if (tube.IsCompleted || tube.IsInOnly)
                {
                    // completed tubes stay; in-only tubes never give a ball back
                    tube.Shake();
                    return;
                }
                tube.OnSelect();
                _curTube = tube;
                return;
            }

            if (tube == _curTube)
            {
                ClearSelection();
                return;
            }

            if (tube.CanImport(_curTube))
            {
                var from = _curTube;
                _curTube = null;
                tube.Import(from, BallMoveSpeed, PerBallDelay, OnMoveEnd);
            }
            else
            {
                // illegal move: both tubes shake, selection is dropped
                var from = _curTube;
                _curTube = null;
                from.CancelSelect();
                from.Shake();
                tube.Shake();
            }
        }

        void ClearSelection()
        {
            if (_curTube != null) _curTube.CancelSelect();
            _curTube = null;
        }

        // =====================================================================
        // Move end / win (reference GameMgr.OnMoveEnd / GameOver / EndGame)
        // =====================================================================

        void OnMoveEnd(Tube from, Tube to, int count)
        {
            _record.Push(new RecordStep { From = from.Index, To = to.Index, Count = count });
            _sumStep++;
            MovesChanged?.Invoke(_sumStep, UndosLeft);

            if (to.IsCompleted)
            {
                to.RevealAll();   // nothing left to guess in a finished tube
                to.PlayTubeFull();
                TubeCompleted?.Invoke(to);
                OpenHiddenTubes(animate: true);
            }

            if (GameOver())
            {
                EndGame();
                return;
            }
            ScheduleNoMoveCheck();
        }

        /// <summary>
        /// Opens every hidden tube next to a completed tube (index - 1 / + 1, mirrors <see cref="BoardState.OpenHiddenTubes"/>);
        /// an opened tube that is already completed opens its other neighbour too.
        /// </summary>
        void OpenHiddenTubes(bool animate)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < _tubes.Count; i++)
                {
                    if (!_tubes[i].IsHiddenTube) continue;
                    if ((i > 0 && _tubes[i - 1].IsCompleted) || (i + 1 < _tubes.Count && _tubes[i + 1].IsCompleted))
                    {
                        _tubes[i].Open(animate);
                        if (_tubes[i].IsCompleted) _tubes[i].RevealAll();
                        HiddenTubeOpened?.Invoke(_tubes[i]);
                        changed = true;
                    }
                }
            }
        }

        /// <summary>Every tube completed; empty tubes count as completed.</summary>
        public bool GameOver()
        {
            foreach (var t in _tubes) if (!t.IsIdentical) return false;
            return true;
        }

        void EndGame()
        {
            _gameOver = true;
            _attemptByLevel.Remove(CurrentLevel);   // a win resets the retry count, so the next visit starts at seed 0 again
            SaveProgress();
            ClearSelection();
            StartCoroutine(DelayGameOver());
        }

        /// <summary>Saves the level after the one just won, so the game reopens there. Never moves progress back.</summary>
        void SaveProgress()
        {
            // editor tests (Test Level, the level editor's Test) leave the player's progress alone
            if (IsTestLevelOn || OverrideLevel != null || IsBotPlaying) return;
            int next = CurrentLevel + 1;
            if (next <= SavedLevel) return;
            PlayerPrefs.SetInt(SavedLevelKey, next);
            PlayerPrefs.Save();
        }

        IEnumerator DelayGameOver()
        {
            yield return new WaitForSeconds(WinDelay);
            LevelWon?.Invoke(CurrentLevel);
        }

        // =====================================================================
        // Dead-end detection (reference NoMoveSystem / CheckNoMove)
        // =====================================================================

        void ScheduleNoMoveCheck()
        {
            if (_noMoveCheck != null) StopCoroutine(_noMoveCheck);
            _noMoveCheck = StartCoroutine(NoMoveRoutine());
        }

        IEnumerator NoMoveRoutine()
        {
            // wait for all animations to settle
            yield return null;
            while (AnyTubeMoving()) yield return null;
            _noMoveCheck = null;
            if (_gameOver) yield break;

            var board = BuildBoard();
            bool dead = !board.HasAnyMove();
            if (!dead)
            {
                var verdict = _solver.Solve(board, new SolverBudget { MaxStates = 120_000, MaxMilliseconds = 250 });
                dead = verdict == SolveVerdict.Unsolvable;
            }
            if (dead)
            {
                yield return new WaitForSeconds(0.4f);
                if (!_gameOver) NoMoves?.Invoke();
            }
        }

        public BoardState BuildBoard()
        {
            var board = new BoardState(_tubes.Count, _capacity);
            for (int i = 0; i < _tubes.Count; i++)
            {
                board.SetKind(i, _tubes[i].Kind);
                foreach (var c in _tubes[i].ColorsBottomFirst()) board.Push(i, (byte)c);
                board.SetHeavy(i, _tubes[i].IsHeavyBottom);
            }
            return board;
        }

        public bool AnyTubeMoving()
        {
            foreach (var t in _tubes) if (t.IsMoving) return true;
            return false;
        }

        // =====================================================================
        // Boosters (reference Undo / TryAddTube)
        // =====================================================================

        public bool Undo()
        {
            if (_gameOver || _record.Count == 0) return false;
            if (AnyTubeMoving()) return false;
            if (UndosLeft <= 0)
            {
                Toast?.Invoke("No undos left");
                return false;
            }
            ClearSelection();
            var step = _record.Pop();
            _undosUsed++;
            _sumStep = Mathf.Max(0, _sumStep - 1);
            var from = _tubes[step.From];
            var to = _tubes[step.To];
            to.MoveBackTo(from, step.Count, BallMoveSpeed, PerBallDelay, null);
            MovesChanged?.Invoke(_sumStep, UndosLeft);
            return true;
        }

        /// <summary>
        /// Paid "continue" from the out-of-moves popup: takes back the last <paramref name="moves"/> moves one after the
        /// other (free of the undo limit), then re-runs the dead-end check so the popup comes back if the board is still stuck.
        /// Returns false when there is nothing to rewind.
        /// </summary>
        public bool Rewind(int moves)
        {
            if (_gameOver || _record.Count == 0 || moves <= 0) return false;
            if (AnyTubeMoving()) return false;
            if (_noMoveCheck != null) { StopCoroutine(_noMoveCheck); _noMoveCheck = null; }
            ClearSelection();
            StartCoroutine(RewindRoutine(Mathf.Min(moves, _record.Count)));
            return true;
        }

        IEnumerator RewindRoutine(int moves)
        {
            for (int i = 0; i < moves && _record.Count > 0; i++)
            {
                var step = _record.Pop();
                _sumStep = Mathf.Max(0, _sumStep - 1);
                bool done = false;
                _tubes[step.To].MoveBackTo(_tubes[step.From], step.Count, BallMoveSpeed, PerBallDelay, () => done = true);
                while (!done) yield return null;
                MovesChanged?.Invoke(_sumStep, UndosLeft);
            }
            ScheduleNoMoveCheck();
        }

        public bool AddTube()
        {
            if (_gameOver) return false;
            if (AddTubesLeft <= 0)
            {
                Toast?.Invoke("No more tubes");
                return false;
            }
            if (_tubes.Count >= BoardState.MaxTubes) return false;
            if (AnyTubeMoving()) return false;   // re-laying out the board would cut the flights short
            ClearSelection();
            _addedTubes++;
            CreateTube(_tubes.Count);
            ComputeTubePos(animate: true);
            AddTubeChanged?.Invoke(AddTubesLeft);
            return true;
        }

        /// <summary>
        /// Little-tube booster (once per level): adds a one-ball tube as a temporary parking spot. It never counts
        /// as a finished tube, so the level is only won once the little tube is empty again.
        /// </summary>
        public bool AddSmallTube()
        {
            if (_gameOver) return false;
            if (SmallTubesLeft <= 0)
            {
                Toast?.Invoke("One little tube per level");
                return false;
            }
            if (_tubes.Count >= BoardState.MaxTubes) return false;
            if (AnyTubeMoving()) return false;
            ClearSelection();
            _addedSmallTubes++;
            CreateSmallTube(_tubes.Count);
            ComputeTubePos(animate: true);
            SmallTubeChanged?.Invoke(SmallTubesLeft);
            return true;
        }

        /// <summary>Solver hint: next move of a found solution, or null.</summary>
        public SolverMove? Hint()
        {
            var board = BuildBoard();
            if (_solver.Solve(board, SolverBudget.Default) == SolveVerdict.Solvable && _solver.Solution != null && _solver.Solution.Count > 0)
                return _solver.Solution[0];
            return null;
        }
    }
}
