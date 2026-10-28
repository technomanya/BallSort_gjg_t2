using System.Collections;
using BallSortGame.Core;
using BallSortGame.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.UI
{
    /// <summary>
    /// In-game HUD wired like the reference GameViewV3: top bar (reset / level / settings),
    /// bottom bar (undo / shuffle / add tube), plus the SuccessView and NoMoveView popups.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        public GameMgr game;

        [Header("Top bar")]
        public Text levelText;
        public Button resetButton;
        public Button settingsButton;
        public GameObject settingsPanel;
        public Button homeButton;
        public Button settingsCloseButton;

        [Header("Bottom bar")]
        public Button undoButton;
        public Text undoCountText;
        public Button shuffleButton;
        public Button addTubeButton;
        public Text addTubeCountText;

        [Header("Popups")]
        public GameObject successView;
        public Text successTitle;
        public Button nextButton;
        public GameObject noMoveView;
        public Button noMoveReplayButton;
        public Button noMoveUndoButton;
        public Button noMoveAddTubeButton;
        public Button noMoveCloseButton;

        [Header("Toast")]
        public GameObject toast;
        public Text toastText;

        Coroutine _toastRoutine;

        void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameMgr>();
            Hook(resetButton, () => game.Restart());
            Hook(settingsButton, () => SetSettings(!(settingsPanel != null && settingsPanel.activeSelf)));
            Hook(settingsCloseButton, () => SetSettings(false));
            Hook(homeButton, () => { SetSettings(false); game.StartLevel(1); });
            Hook(undoButton, () => game.Undo());
            Hook(shuffleButton, () => ShowToast("Shuffle is not part of this example"));
            Hook(addTubeButton, () => game.AddTube());
            Hook(nextButton, () => { Show(successView, false); game.NextLevel(); });
            Hook(noMoveReplayButton, () => { Show(noMoveView, false); game.Restart(); });
            Hook(noMoveUndoButton, () => { Show(noMoveView, false); game.Undo(); });
            Hook(noMoveAddTubeButton, () => { Show(noMoveView, false); game.AddTube(); });
            Hook(noMoveCloseButton, () => Show(noMoveView, false));
            Show(successView, false);
            Show(noMoveView, false);
            Show(toast, false);
            SetSettings(false);
        }

        void OnEnable()
        {
            if (game == null) return;
            game.LevelStarted += OnLevelStarted;
            game.MovesChanged += OnMovesChanged;
            game.AddTubeChanged += OnAddTubeChanged;
            game.LevelWon += OnLevelWon;
            game.NoMoves += OnNoMoves;
            game.Toast += ShowToast;
        }

        void OnDisable()
        {
            if (game == null) return;
            game.LevelStarted -= OnLevelStarted;
            game.MovesChanged -= OnMovesChanged;
            game.AddTubeChanged -= OnAddTubeChanged;
            game.LevelWon -= OnLevelWon;
            game.NoMoves -= OnNoMoves;
            game.Toast -= ShowToast;
        }

        static void Hook(Button b, UnityEngine.Events.UnityAction a)
        {
            if (b != null) b.onClick.AddListener(a);
        }

        static void Show(GameObject go, bool on)
        {
            if (go != null) go.SetActive(on);
        }

        void SetSettings(bool on)
        {
            Show(settingsPanel, on);
            if (game != null) game.IsPaused = on;
        }

        void OnLevelStarted(int level, LevelData data)
        {
            if (levelText != null) levelText.text = $"Level {level}";
            Show(successView, false);
            Show(noMoveView, false);
        }

        void OnMovesChanged(int moves, int undosLeft)
        {
            if (undoCountText != null) undoCountText.text = undosLeft.ToString();
            if (undoButton != null) undoButton.interactable = undosLeft > 0 && moves > 0;
        }

        void OnAddTubeChanged(int left)
        {
            if (addTubeCountText != null) addTubeCountText.text = left.ToString();
            if (addTubeButton != null) addTubeButton.interactable = left > 0;
        }

        void OnLevelWon(int level)
        {
            if (successTitle != null) successTitle.text = "Well Done!";
            Show(successView, true);
        }

        void OnNoMoves()
        {
            Show(noMoveView, true);
        }

        public void ShowToast(string message)
        {
            if (toast == null || toastText == null) return;
            toastText.text = message;
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastRoutine = StartCoroutine(ToastRoutine());
        }

        IEnumerator ToastRoutine()
        {
            toast.SetActive(true);
            yield return new WaitForSeconds(1.4f);
            toast.SetActive(false);
            _toastRoutine = null;
        }
    }
}
