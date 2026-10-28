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
    public class GameMgr : MonoBehaviour
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
        [Tooltip("Color index 1 uses element 0, index 2 element 1, ...")]
        public Sprite[] ballSprites;
        public float TubeWidth = 1.28f;
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
        public string levelPackResource = "data/lv_1_100";
        [Min(1)] public int startLevel = 1;
        public int MaxAddTube = 1;
        public int UndoLimit = 5;

        [Header("Scene")]
        public Camera cam;
        public Transform tubesRoot;
        [Tooltip("Extra world units kept free around the widest row when fitting the camera.")]
        public float HorizontalMargin = 0.35f;
        [Tooltip("Fraction of the camera's vertical half-size the board is shifted down to leave room for the top bar.")]
        public float BoardOffsetFactor = -0.05f;
        [Tooltip("Screen height fraction covered by the HUD top bar (172 of 2340 px in the reference layout); flight arcs stay below it.")]
        public float TopBarScreenFraction = 0.075f;
        [Tooltip("Extra world units kept between the highest point of a flight arc and whatever is above it.")]
        public float ArcSafetyMargin = 0.15f;

        // ---------- events for the HUD ----------
        public event Action<int, LevelData> LevelStarted;
        public event Action<int, int> MovesChanged;   // (moves, undosLeft)
        public event Action<int> AddTubeChanged;      // add tubes left
        public event Action<int> LevelWon;
        public event Action NoMoves;
        public event Action<string> Toast;

        public int CurrentLevel { get; private set; }
        public LevelData CurrentLevelData { get; private set; }
        public IReadOnlyList<Tube> Tubes => _tubes;
        public bool IsPaused { get => _isPause; set { _isPause = value; if (value) ClearSelection(); } }
        public int UndosLeft => Mathf.Max(0, UndoLimit - _undosUsed);
        public int AddTubesLeft => Mathf.Max(0, MaxAddTube - _addedTubes);

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
                Debug.LogError("GameMgr: no levels loaded.");
                return;
            }
            StartLevel(startLevel);
        }

        void Update()
        {
            if (_isPause || _gameOver) return;
            if (!Input.GetMouseButtonDown(0)) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (EventSystem.current != null && Input.touchCount > 0 && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)) return;

            Vector3 wp = cam.ScreenToWorldPoint(Input.mousePosition);
            var hit = Physics2D.OverlapPoint(new Vector2(wp.x, wp.y));
            if (hit == null) return;
            var tube = hit.GetComponent<Tube>();
            if (tube != null) OnTubeClick(tube);
        }

        // =====================================================================
        // Level lifecycle
        // =====================================================================

        public void StartLevel(int level)
        {
            if (_stage == null) return;
            int idx = ((level - 1) % _stage.Levels.Count + _stage.Levels.Count) % _stage.Levels.Count;
            CurrentLevel = level;
            // Generated packs carry several fills ("seeds") per level: attempt k plays seed k, then a random
            // one that is not the last played; easier dlb seeds take over after the cap of failed attempts.
            var slot = _stage.Levels[idx];
            _attemptByLevel.TryGetValue(level, out int attempt);
            _lastSeedByLevel.TryGetValue(level, out int lastSeed);
            CurrentLevelData = slot.ForAttempt(attempt, attempt == 0 ? -1 : lastSeed, _seedRng, out int seedIndex);
            _attemptByLevel[level] = attempt + 1;
            _lastSeedByLevel[level] = seedIndex;
            StartGameInternal(CurrentLevelData);
            LevelStarted?.Invoke(CurrentLevel, CurrentLevelData);
            MovesChanged?.Invoke(_sumStep, UndosLeft);
            AddTubeChanged?.Invoke(AddTubesLeft);
        }

        public void Restart() => StartLevel(CurrentLevel);
        public void NextLevel() => StartLevel(CurrentLevel + 1);

        void StartGameInternal(LevelData level)
        {
            if (_noMoveCheck != null) { StopCoroutine(_noMoveCheck); _noMoveCheck = null; }
            StopAllCoroutines();
            ClearTubes();
            _record.Clear();
            _sumStep = 0;
            _undosUsed = 0;
            _addedTubes = 0;
            _curTube = null;
            _gameOver = false;
            _isPause = false;
            _capacity = Mathf.Max(1, level.Capacity);

            for (int i = 0; i < level.Tubes.Count; i++)
            {
                var tube = CreateTube(i);
                // level tubes are listed top-first; the Tube wants bottom-first colors
                var topFirst = level.Tubes[i];
                var bottomFirst = new List<int>(topFirst.Count);
                for (int j = topFirst.Count - 1; j >= 0; j--) if (topFirst[j] > 0) bottomFirst.Add(topFirst[j]);
                tube.SetBalls(bottomFirst, CreateBall);
            }
            ComputeTubePos(animate: false);
        }

        Tube CreateTube(int index)
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
            tube.Setup(index, _capacity, tubeSprite, 3);
            _tubes.Add(tube);
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

            // camera: fit the widest row horizontally and all rows vertically
            float aspect = cam != null ? cam.aspect : 9f / 16f;
            float size = 5f;
            size = Mathf.Max(size, (maxRowWidth / 2f + HorizontalMargin) / aspect);
            size = Mathf.Max(size, rows * RowGap / 2f + 2.6f);
            if (cam != null)
            {
                cam.orthographic = true;
                cam.orthographicSize = size;
            }
            float boardY = size * BoardOffsetFactor;
            tubesRoot.position = new Vector3(0f, boardY, 0f);

            // free space above each row: up to the top bar for the first row, up to the row above otherwise.
            // Balls use it for an elliptic flight arc when there is enough of it.
            float camTop = (cam != null ? cam.transform.position.y : 0f) + size;
            float topBarWorld = 2f * size * TopBarScreenFraction;
            for (int i = 0; i < n; i++)
            {
                _tubes[i].transform.localPosition = new Vector3(positions[i].x, positions[i].y, 0f);
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
                if (tube.IsEmpty) return;
                if (tube.IsCompleted)
                {
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

            if (to.IsCompleted) to.PlayTubeFull();

            if (GameOver())
            {
                EndGame();
                return;
            }
            ScheduleNoMoveCheck();
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
            ClearSelection();
            StartCoroutine(DelayGameOver());
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
                foreach (var c in _tubes[i].ColorsBottomFirst()) board.Push(i, (byte)c);
            return board;
        }

        bool AnyTubeMoving()
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

        public bool AddTube()
        {
            if (_gameOver) return false;
            if (AddTubesLeft <= 0)
            {
                Toast?.Invoke("No more tubes");
                return false;
            }
            if (_tubes.Count >= BoardState.MaxTubes) return false;
            ClearSelection();
            _addedTubes++;
            CreateTube(_tubes.Count);
            ComputeTubePos(animate: true);
            AddTubeChanged?.Invoke(AddTubesLeft);
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
