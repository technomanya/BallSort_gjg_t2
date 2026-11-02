using System;
using System.Collections;
using System.Collections.Generic;
using BallSortGame.Core;
using BallSortGame.Haptics;
using UnityEngine;

namespace BallSortGame.Gameplay
{
    /// <summary>
    /// Runtime game loop: builds the board, handles taps and moves, checks for a win or a dead end, and runs the boosters.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public struct RecordStep
        {
            public int From, To, Count;
            /// <summary>Hidden tubes this move opened (bit i = tube i), so an undo can cover them again.</summary>
            public long Opened;
        }

        [Header("Prefabs")]
        public Tube tubePrefab;
        public Ball ballPrefab;

        [Header("Skin")]
        public Sprite tubeSprite;
        [Tooltip("Glass of the little booster tube.")]
        public Sprite smallTubeSprite;
        [Tooltip("Shifts the little tube's ball up (+) or down (-).")]
        public float SmallTubeBallOffset = 0f;
        [Tooltip("Up arrow under one-way tubes. Empty = UISkin.iconArrow.")]
        public Sprite arrowSprite;
        [Tooltip("Cover over a hidden tube. Empty = green-tinted glass.")]
        public Sprite hiddenTubeSprite;
        [Tooltip("Color index 1 uses element 0, index 2 element 1, ...")]
        public Sprite[] ballSprites;
        [Tooltip("Weight drawn over a heavy ball.")]
        public Sprite heavyBallSprite;
        public float TubeWidth = 1.81f;
        public float TubeHeight = 4.8f;
        public float TubeSpace = 0.42f;
        public float FirstBallY = -1.79f;
        public float BallPitch = 1.08f;
        public float LiftY = 3.05f;
        [Tooltip("Shorter rows keep this row width and spread out.")]
        public int AvgSpaceCount = 5;
        public int MaxTubesPerRow = Core.TubeRows.DefaultMaxPerRow;
        public float RowGap = 6.3f;

        [Header("Motion")]
        public float BallMoveSpeed = 26f;
        public float PerBallDelay = 0.045f;
        public float WinDelay = 0.6f;

        [Header("Level")]
        public string levelPackResource = "data/levels";
        public int MaxAddTube = 1;
        [Tooltip("Little one-ball parking tubes per level.")]
        public int MaxAddSmallTube = 1;
        public int UndoLimit = 5;

        [Header("Editor Test")]
        [Tooltip("Editor only: start at Test Level. Wins are not saved.")]
        public bool useTestLevel;
        [Min(1)] public int testLevel = 1;

        [Header("Scene")]
        public Camera cam;
        public Transform tubesRoot;
        [Tooltip("Free world units around the widest row.")]
        public float HorizontalMargin = 0.35f;
        [Tooltip("Board shift as a fraction of camera half-size.")]
        public float BoardOffsetFactor = -0.05f;
        [Tooltip("Screen share of the HUD top bar. HUD sets it at runtime.")]
        public float TopBarScreenFraction = 0.129f;
        [Tooltip("Screen share of the booster bar. HUD sets it at runtime.")]
        public float BottomBarScreenFraction = 0.128f;
        [Tooltip("Gap between a flight arc's peak and what is above.")]
        public float ArcSafetyMargin = 0.15f;
        /// <summary>The level editor raises this so its controls stay clear of the board.</summary>
        [NonSerialized] public float MinCameraSize;

        // HUD events
        public event Action<int, LevelData> LevelStarted;
        public event Action<int, int> MovesChanged;   // (moves, undosLeft)
        public event Action<int> AddTubeChanged;      // add tubes left
        public event Action<int> SmallTubeChanged;    // little tubes left
        public event Action<int> LevelWon;
        public event Action<Tube> TubeCompleted;
        public event Action NoMoves;
        public event Action<string> Toast;

        const string SavedLevelKey = "ballsort.level";

        /// <summary>One past the highest level won, 1 for a new player.</summary>
        public static int SavedLevel => Mathf.Max(1, PlayerPrefs.GetInt(SavedLevelKey, 1));

        public static void ClearSavedLevel()
        {
            PlayerPrefs.DeleteKey(SavedLevelKey);
            PlayerPrefs.Save();
        }

        /// <summary>Editor Test Level is on, so progress is not saved.</summary>
        public bool IsTestLevelOn => Application.isEditor && useTestLevel;

        public int CurrentLevel { get; private set; }
        public LevelData CurrentLevelData { get; private set; }
        /// <summary>Earlier tries of this level: 0 on the first try, +1 per restart or retry.</summary>
        public int CurrentAttempt { get; private set; }
        /// <summary>First try in a real play, not a test or a bot run.</summary>
        public bool IsOneShotPlay => CurrentAttempt == 0 && !IsTestLevelOn && OverrideLevel == null && !IsBotPlaying;
        /// <summary>Loaded level pack, null when it failed to load.</summary>
        public StageData Stage => _stage;
        /// <summary>Level editor's Test level, played instead of the pack. Null = play the pack.</summary>
        public LevelData OverrideLevel { get; private set; }
        /// <summary>The board is won. The win delay, splash and popup follow.</summary>
        public bool IsGameOver => _gameOver;
        public IReadOnlyList<Tube> Tubes => _tubes;
        public bool IsPaused { get => _isPause; set { _isPause = value; if (value) ClearSelection(); } }
        public int UndosLeft => Mathf.Max(0, UndoLimit - _undosUsed);
        public int AddTubesLeft => Mathf.Max(0, MaxAddTube - _addedTubes);
        public int SmallTubesLeft => Mathf.Max(0, MaxAddSmallTube - _addedSmallTubes);
        /// <summary>
        /// The level bot drives the board. Stays true until another level starts, so a move or win that lands after
        /// STOP saves no progress and pays no rewards.
        /// </summary>
        public bool IsBotPlaying
        {
            get => _botPlaying || _botLevel;
            set { _botPlaying = value; if (value) _botLevel = true; }
        }
        bool _botPlaying, _botLevel;
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
        bool _rewinding;        // a tap mid-rewind would snap balls in flight, so input waits
        bool _layoutPending;    // HUD bands changed mid-flight; re-layout once the balls land
        int _sumStep;
        int _undosUsed;
        int _addedTubes;
        int _addedSmallTubes;
        int _capacity;
        Coroutine _noMoveCheck;
        // Boards played this attempt. A lost board fails once no move leads somewhere new.
        readonly HashSet<ulong> _seenBoards = new HashSet<ulong>();

        void Awake()
        {
            Time.timeScale = 1f; // the Editor keeps a debug time scale across play sessions
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
            if (_layoutPending && !AnyTubeMoving())
            {
                _layoutPending = false;
                ComputeTubePos(animate: false);
            }
            if (_isPause || _gameOver || IsBotPlaying) return;
            if (!PointerInput.Down) return;
            if (IsPointerOverUi()) return;

            Vector3 wp = cam.ScreenToWorldPoint(PointerInput.Position);
            var hit = Physics2D.OverlapPoint(new Vector2(wp.x, wp.y));
            if (hit == null) return;
            var tube = hit.GetComponent<Tube>();
            if (tube != null) OnTubeClick(tube);
        }

        public static bool IsPointerOverUi() => PointerInput.IsOverUi();

        // --- level lifecycle

        public void StartLevel(int level)
        {
            if (OverrideLevel == null && (_stage == null || _stage.Levels.Count == 0)) return;
            CurrentLevel = level;
            // Each level has several fills (seeds). Attempt k plays seed k, then a random one other than the last.
            // Easier dlb seeds take over after enough failed attempts.
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
        /// Level bot: plays one fill of a pack level (0 = main seed, 1.. variants, 1000+ dlb fills). Ignores the
        /// override level and the retry counters.
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
            Haptic.Play(HapticType.LevelStart);
            LevelStarted?.Invoke(CurrentLevel, data);
            MovesChanged?.Invoke(_sumStep, UndosLeft);
            AddTubeChanged?.Invoke(AddTubesLeft);
            SmallTubeChanged?.Invoke(SmallTubesLeft);
        }

        /// <summary>Level editor: plays this level in place of the pack slot from seed 0. Null goes back to the pack.</summary>
        public void SetOverrideLevel(LevelData level)
        {
            OverrideLevel = level;
            int lv = Mathf.Max(1, CurrentLevel);
            _attemptByLevel.Remove(lv);
            _lastSeedByLevel.Remove(lv);
            StartLevel(lv);
        }

        /// <summary>
        /// Level editor: shows a layout on the board without starting a game, paused until a level starts.
        /// Tubes are bottom first and a cell below 1 is an unknown slot. heavy is a per-tube bit mask, hiddenBalls
        /// is per-tube 0/1 bottom first. Null lists mean none.
        /// </summary>
        public void Preview(List<List<int>> tubesBottomFirst, int capacity, IList<int> kinds, IList<int> heavy, IList<List<int>> hiddenBalls)
        {
            if (_noMoveCheck != null) { StopCoroutine(_noMoveCheck); _noMoveCheck = null; }
            StopAllCoroutines();
            WinSplash.Current?.Cancel();   // its Play ran inside the coroutine just stopped
            _rewinding = false;
            _botLevel = _botPlaying;
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
                if (heavy != null && i < heavy.Count && heavy[i] > 0)
                    for (int k = 0; k < tube.Count && k < 31; k++)
                        if (((heavy[i] >> k) & 1) != 0) tube.Balls[k].SetHeavy(true, heavyBallSprite);
                if (hiddenBalls != null && i < hiddenBalls.Count && hiddenBalls[i] != null)
                    for (int k = 0; k < hiddenBalls[i].Count && k < tube.Count; k++)
                        if (hiddenBalls[i][k] != 0 && tubesBottomFirst[i][k] > 0) tube.Balls[k].ShowHiddenMark();
                if (tube.IsHiddenTube) tube.SetCoverAlpha(0.55f);   // see-through so the balls under it can be painted
            }
            ComputeTubePos(animate: false);
        }

        // Grey "?" ball for the editor's unknown cells.
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

        /// <summary>Plays testLevel and turns Test Level on, so wins are not saved.</summary>
        public void PlayTestLevel()
        {
            useTestLevel = true;
            StartLevel(testLevel);
        }

        void StartGameInternal(LevelData level)
        {
            if (_noMoveCheck != null) { StopCoroutine(_noMoveCheck); _noMoveCheck = null; }
            StopAllCoroutines();
            WinSplash.Current?.Cancel();   // its Play ran inside the coroutine just stopped
            _rewinding = false;
            _botLevel = _botPlaying;
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
                // level data is top first, Tube wants bottom first
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
                // hidden balls stay grey until they reach the top
                for (int k = 0; k < hiddenBottomFirst.Count; k++)
                    if (hiddenBottomFirst[k]) tube.Balls[k].Hide(HiddenBallSprite);
                for (int k = 0; k < tube.Count; k++)
                    if (level.IsHeavyAt(i, k)) tube.Balls[k].SetHeavy(true, heavyBallSprite);
                tube.RevealTop();
                if (tube.IsCompleted) tube.RevealAll();
            }
            OpenHiddenTubes(animate: false);   // a tube completed from the start opens its neighbours at once
            ComputeTubePos(animate: false);
            _seenBoards.Clear();
            _seenBoards.Add(BuildBoard().ComputeHash());
        }

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

        // One-ball tube on the row's floor. Its lift height matches the big tubes so flights look the same.
        Tube CreateSmallTube(int index)
        {
            var tube = NewTube();
            float h = smallTubeSprite != null ? smallTubeSprite.bounds.size.y : TubeHeight;
            float drop = (TubeHeight - h) / 2f;   // its centre sits this much lower than a big tube's
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

        // --- layout

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

        // Screen share between the HUD bars on a 1080x2340 screen.
        const float ReferenceHudBand = 1f - 0.129f - 0.128f;

        /// <summary>
        /// The HUD reports the measured screen share of its top and bottom bars, so the camera fit and flight arcs
        /// follow the real layout on every screen.
        /// </summary>
        public void SetHudBands(float top, float bottom)
        {
            if (Mathf.Abs(top - TopBarScreenFraction) < 0.002f && Mathf.Abs(bottom - BottomBarScreenFraction) < 0.002f) return;
            TopBarScreenFraction = top;
            BottomBarScreenFraction = bottom;
            // re-seating the balls now would cut flights short and leave their tubes "moving" for good
            if (AnyTubeMoving()) { _layoutPending = true; return; }
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

            // Fit the widest row and all rows. The 2.6 margin suits 1080x2340. When the HUD bars take a bigger
            // share of a shorter screen, zoom out by the same ratio.
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
            // centre the board between the bars, plus the tuned offset
            float boardY = size * (BoardOffsetFactor + BottomBarScreenFraction - TopBarScreenFraction);
            tubesRoot.position = new Vector3(0f, boardY, 0f);

            // free space above each row (to the top bar or the row above) for the flight arcs
            float camTop = (cam != null ? cam.transform.position.y : 0f) + size;
            float topBarWorld = 2f * size * TopBarScreenFraction;
            for (int i = 0; i < n; i++)
            {
                // the little tube stands on the same floor as the big ones
                float drop = _tubes[i].IsSpare ? (_tubes[i].TubeHeight - TubeHeight) / 2f : 0f;
                _tubes[i].transform.localPosition = new Vector3(positions[i].x, positions[i].y + drop, 0f);
                _tubes[i].SnapBalls();
                int r = rowOf[i];
                float liftTop = boardY + positions[i].y + LiftY + 0.5f;
                float ceiling = r == 0 ? camTop - topBarWorld : boardY + rowY[r - 1] - TubeHeight / 2f;
                _tubes[i].ArcHeadroom = Mathf.Max(0f, ceiling - liftTop - ArcSafetyMargin);
            }
        }

        // --- input

        public void OnTubeClick(Tube tube)
        {
            if (_isPause || _gameOver || _rewinding || tube == null) return;

            // a tube still animating cannot be used, so the tap just drops the selection
            if (tube.IsMoving)
            {
                ClearSelection();
                return;
            }

            if (_curTube == null)
            {
                if (tube.IsHiddenTube) { tube.Shake(); return; }
                if (tube.IsEmpty) return;
                if (tube.IsCompleted || tube.IsInOnly)
                {
                    tube.Shake();
                    return;
                }
                tube.OnSelect();
                Haptic.Play(HapticType.TubeSelect);
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
                Haptic.Play(HapticType.PourStart);
                tube.Import(from, BallMoveSpeed, PerBallDelay, OnMoveEnd);
            }
            else
            {
                var from = _curTube;
                _curTube = null;
                from.CancelSelect();
                from.Shake();
                tube.Shake();
            }
        }

        void ClearSelection()
        {
            if (_curTube != null)
            {
                _curTube.CancelSelect();
                Haptic.Play(HapticType.TubeDeselect);
            }
            _curTube = null;
        }

        // --- move end and win

        void OnMoveEnd(Tube from, Tube to, int count)
        {
            long opened = 0;
            if (to.IsCompleted)
            {
                to.RevealAll();
                to.PlayTubeFull();
                TubeCompleted?.Invoke(to);
                opened = OpenHiddenTubes(animate: true);
            }
            _record.Push(new RecordStep { From = from.Index, To = to.Index, Count = count, Opened = opened });
            _sumStep++;
            Haptic.Play(HapticType.PourEnd);
            MovesChanged?.Invoke(_sumStep, UndosLeft);

            if (GameOver())
            {
                EndGame();
                return;
            }
            ScheduleNoMoveCheck();
        }

        // Opens every hidden tube next to a completed one in the same row (row ends do not wrap). An opened tube that
        // is already completed opens its other neighbour too. Keep in sync with BoardState.OpenHiddenTubes.
        long OpenHiddenTubes(bool animate)
        {
            long opened = 0;
            bool changed = true;
            int n = _tubes.Count;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < n; i++)
                {
                    if (!_tubes[i].IsHiddenTube) continue;
                    int l = Core.TubeRows.Left(i, n, MaxTubesPerRow), r = Core.TubeRows.Right(i, n, MaxTubesPerRow);
                    if ((l >= 0 && _tubes[l].IsCompleted) || (r >= 0 && _tubes[r].IsCompleted))
                    {
                        _tubes[i].Open(animate);
                        if (_tubes[i].IsCompleted) _tubes[i].RevealAll();
                        if (i < 63) opened |= 1L << i;
                        changed = true;
                    }
                }
            }
            return opened;
        }

        // Undo covers the tubes its move opened, so undo never opens a tube for free.
        void CloseOpenedTubes(long opened)
        {
            for (int i = 0; i < _tubes.Count && i < 63; i++)
                if (((opened >> i) & 1L) != 0) _tubes[i].Close(hiddenTubeSprite);
        }

        /// <summary>Every tube is completed or empty.</summary>
        public bool GameOver()
        {
            foreach (var t in _tubes) if (!t.IsIdentical) return false;
            return true;
        }

        void EndGame()
        {
            // two flights that finish the board together both land here, so win and pay out once
            if (_gameOver) return;
            _gameOver = true;
            Haptic.Play(HapticType.LevelWin);
            _attemptByLevel.Remove(CurrentLevel);   // the next visit starts at seed 0 again
            SaveProgress();
            ClearSelection();
            StartCoroutine(DelayGameOver());
        }

        // Saves the next level so the game reopens there. Never moves progress back. Tests and bot runs do not save.
        void SaveProgress()
        {
            if (IsTestLevelOn || OverrideLevel != null || IsBotPlaying) return;
            int next = CurrentLevel + 1;
            if (next <= SavedLevel) return;
            PlayerPrefs.SetInt(SavedLevelKey, next);
            PlayerPrefs.Save();
        }

        IEnumerator DelayGameOver()
        {
            yield return new WaitForSeconds(WinDelay);
            var splash = IsBotPlaying ? null : WinSplash.Current;
            if (splash != null) yield return splash.Play();
            LevelWon?.Invoke(CurrentLevel);
        }

        // --- dead-end detection

        void ScheduleNoMoveCheck()
        {
            if (_noMoveCheck != null) StopCoroutine(_noMoveCheck);
            _noMoveCheck = StartCoroutine(NoMoveRoutine());
        }

        IEnumerator NoMoveRoutine()
        {
            yield return null;
            while (AnyTubeMoving()) yield return null;
            _noMoveCheck = null;
            if (_gameOver) yield break;

            // Fail only when no useful move is left, even if the solver already knows the board is lost.
            // Pouring back to a position already played does not count, so a lost board cannot live forever.
            var board = BuildBoard();
            _seenBoards.Add(board.ComputeHash());
            bool dead = !board.HasUsefulMove();
            if (!dead && !board.HasNewMove(_seenBoards))
            {
                var verdict = _solver.Solve(board, new SolverBudget { MaxStates = 120_000, MaxMilliseconds = 250 });
                dead = verdict == SolveVerdict.Unsolvable;
            }
            if (dead)
            {
                yield return new WaitForSeconds(0.4f);
                if (!_gameOver)
                {
                    Haptic.Play(HapticType.LevelLose);
                    NoMoves?.Invoke();
                }
            }
        }

        public BoardState BuildBoard()
        {
            var board = new BoardState(_tubes.Count, _capacity) { MaxTubesPerRow = MaxTubesPerRow };
            for (int i = 0; i < _tubes.Count; i++)
            {
                board.SetKind(i, _tubes[i].Kind);
                foreach (var c in _tubes[i].ColorsBottomFirst()) board.Push(i, (byte)c);
                board.SetHeavyMask(i, _tubes[i].HeavyMask);
                board.SetUnseenMask(i, _tubes[i].HiddenBallMask);   // a run stops at a "?" ball
            }
            return board;
        }

        public bool AnyTubeMoving()
        {
            foreach (var t in _tubes) if (t.IsMoving) return true;
            return false;
        }

        // --- boosters

        public bool Undo()
        {
            if (_gameOver || _rewinding || _record.Count == 0) return false;
            if (AnyTubeMoving()) return false;
            if (UndosLeft <= 0)
            {
                Toast?.Invoke("No undos left");
                return false;
            }
            if (_noMoveCheck != null) { StopCoroutine(_noMoveCheck); _noMoveCheck = null; }   // no popup for the board being taken back
            ClearSelection();
            _seenBoards.Clear();   // the taken-back positions may be played again
            var step = _record.Pop();
            _undosUsed++;
            _sumStep = Mathf.Max(0, _sumStep - 1);
            var from = _tubes[step.From];
            var to = _tubes[step.To];
            CloseOpenedTubes(step.Opened);
            // the check re-records the landed board, or pouring back and forth could keep a dead board alive
            to.MoveBackTo(from, step.Count, BallMoveSpeed, PerBallDelay, ScheduleNoMoveCheck);
            MovesChanged?.Invoke(_sumStep, UndosLeft);
            return true;
        }

        /// <summary>
        /// Paid continue from the out-of-moves popup: takes back the last moves, ignoring the undo limit, then re-checks
        /// for a dead end. False when there is nothing to rewind.
        /// </summary>
        public bool Rewind(int moves)
        {
            if (_gameOver || _rewinding || _record.Count == 0 || moves <= 0) return false;
            if (AnyTubeMoving()) return false;
            if (_noMoveCheck != null) { StopCoroutine(_noMoveCheck); _noMoveCheck = null; }
            ClearSelection();
            _rewinding = true;   // a tap mid-rewind would snap balls in flight and lock their tubes
            StartCoroutine(RewindRoutine(Mathf.Min(moves, _record.Count)));
            return true;
        }

        IEnumerator RewindRoutine(int moves)
        {
            _seenBoards.Clear();   // the taken-back positions may be played again
            for (int i = 0; i < moves && _record.Count > 0; i++)
            {
                var step = _record.Pop();
                _sumStep = Mathf.Max(0, _sumStep - 1);
                CloseOpenedTubes(step.Opened);
                bool done = false;
                _tubes[step.To].MoveBackTo(_tubes[step.From], step.Count, BallMoveSpeed, PerBallDelay, () => done = true);
                while (!done) yield return null;
                MovesChanged?.Invoke(_sumStep, UndosLeft);
            }
            _rewinding = false;
            ScheduleNoMoveCheck();
        }

        public bool AddTube()
        {
            if (_gameOver || _rewinding) return false;
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
            OpenHiddenTubes(animate: true);   // the rows re-flow, so a completed tube may now sit next to a hidden one
            AddTubeChanged?.Invoke(AddTubesLeft);
            ScheduleNoMoveCheck();   // records the board with the new tube as a played position
            return true;
        }

        /// <summary>
        /// Adds a one-ball parking tube. It never counts as finished, so the level is won only once it is empty again.
        /// </summary>
        public bool AddSmallTube()
        {
            if (_gameOver || _rewinding) return false;
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
            OpenHiddenTubes(animate: true);   // the rows re-flow, so a completed tube may now sit next to a hidden one
            SmallTubeChanged?.Invoke(SmallTubesLeft);
            ScheduleNoMoveCheck();   // records the board with the little tube as a played position
            return true;
        }

    }
}
