using System.Collections;
using BallSortGame.Core;
using BallSortGame.Gameplay;
using BallSortGame.Haptics;
using BallSortGame.MiniGame;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.UI
{
    /// <summary>
    /// In-game HUD: top bar, booster bar, the settings page and the win and out-of-moves popups.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        public GameManager game;

        [Header("Top bar")]
        public Text levelText;
        [Tooltip("Top left: restarts the level.")]
        public Button resetButton;
        public Button settingsButton;
        [Tooltip("Optional coin button that opens Mana Flip.")]
        public Button miniGameButton;
        public GameObject settingsPanel;
        public Button homeButton;
        public Button settingsCloseButton;

        [Header("Booster bar")]
        public Button undoButton;
        public Text undoCountText;
        public Button shuffleButton;
        public Button addTubeButton;
        public Text addTubeCountText;
        [Tooltip("Adds the one-ball little tube (once per level).")]
        public Button smallTubeButton;
        public Text smallTubeCountText;
        [Tooltip("The 'i' on the bar, toggles the booster info panel.")]
        public Button boosterInfoButton;
        public GameObject boosterInfoPanel;

        [Header("Popups")]
        public GameObject successView;
        public Text successTitle;
        public Button nextButton;
        public GameObject noMoveView;
        public Button noMoveReplayButton;
        public Button noMoveHomeButton;
        public Button noMoveUndoButton;
        public Button noMoveAddTubeButton;
        public Button noMoveCloseButton;
        [Tooltip("Paid continue: takes back a few moves for coins.")]
        public Button noMoveContinueButton;
        public Text noMoveContinueCostText;
        [Tooltip("Coins charged by the continue button.")]
        public int continueCost = 900;
        [Tooltip("Moves taken back by the continue button.")]
        public int continueRewindMoves = 3;

        [Header("Main menu")]
        [Tooltip("Opened by Home. Without it Home restarts level 1.")]
        public MainMenuView mainMenu;

        [Header("Toast")]
        public GameObject toast;
        public Text toastText;

        Coroutine _toastRoutine;
        Canvas _canvas;
        RectTransform _levelPill, _boosterBar;
        Rect _measuredSafe;
        Vector2Int _measuredScreen;
        readonly Vector3[] _corners = new Vector3[4];

        void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            // keep the bars clear of the cutout / status bar at the top and the gesture bar at the bottom
            foreach (string bar in new[] { "top_bar", "booster_bar", "booster_info", "toast" })
                SafeAreaFitter.Attach(transform.Find(bar));
            Hook(resetButton, () => { if (!WinPending) game.Restart(); });
            Hook(settingsButton, () => SetSettings(!(settingsPanel != null && settingsPanel.activeSelf)));
            Hook(miniGameButton, OpenMiniGame);
            Hook(settingsCloseButton, () => SetSettings(false));
            Hook(homeButton, GoHome);
            Hook(boosterInfoButton, () => Show(boosterInfoPanel, !(boosterInfoPanel != null && boosterInfoPanel.activeSelf)));
            if (boosterInfoPanel != null && boosterInfoPanel.TryGetComponent(out Button infoClose)) Hook(infoClose, () => Show(boosterInfoPanel, false));
            Hook(undoButton, () => game.Undo());
            Hook(shuffleButton, () => ShowToast("Shuffle is not part of this example"));
            Hook(addTubeButton, () => game.AddTube());
            Hook(smallTubeButton, () => game.AddSmallTube());
            Hook(nextButton, () => { Show(successView, false); game.NextLevel(); });
            Hook(noMoveReplayButton, () => { Show(noMoveView, false); game.Restart(); });
            Hook(noMoveHomeButton, () => { Show(noMoveView, false); GoHome(); });
            Hook(noMoveUndoButton, () => { Show(noMoveView, false); game.Undo(); });
            Hook(noMoveAddTubeButton, () => { Show(noMoveView, false); game.AddTube(); });
            Hook(noMoveCloseButton, () => Show(noMoveView, false));
            Hook(noMoveContinueButton, ContinueForCoins);
            if (noMoveContinueCostText != null) noMoveContinueCostText.text = continueCost + " x";
            Show(successView, false);
            Show(noMoveView, false);
            Show(toast, false);
            Show(boosterInfoPanel, false);
            SetSettings(false);
        }

        // hands the screen share of the bars to the game, so the camera fit follows the real layout
        // (the safe area moves the bars, and short or wide screens give them a bigger share)
        void LateUpdate()
        {
            if (game == null) return;
            if (Screen.safeArea == _measuredSafe && Screen.width == _measuredScreen.x && Screen.height == _measuredScreen.y) return;
            _measuredSafe = Screen.safeArea;
            _measuredScreen = new Vector2Int(Screen.width, Screen.height);
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_levelPill == null && levelText != null) _levelPill = levelText.transform.parent as RectTransform;
            if (_boosterBar == null) _boosterBar = transform.Find("booster_bar") as RectTransform;
            if (_levelPill == null || _boosterBar == null || Screen.height <= 0) return;
            var camera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
            float top = 1f - EdgeY(_levelPill, camera, bottomEdge: true) / Screen.height;
            float bottom = EdgeY(_boosterBar, camera, bottomEdge: false) / Screen.height;
            game.SetHudBands(Mathf.Clamp01(top), Mathf.Clamp01(bottom));
        }

        // screen y of a rect's bottom or top edge
        float EdgeY(RectTransform rt, Camera camera, bool bottomEdge)
        {
            rt.GetWorldCorners(_corners);   // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
            Vector3 corner = _corners[bottomEdge ? 0 : 1];
            return RectTransformUtility.WorldToScreenPoint(camera, corner).y;
        }

        void GoHome()
        {
            if (WinPending) return;
            SetSettings(false);
            if (mainMenu != null) mainMenu.Open();
            else game.StartLevel(1);
        }

        void OpenMiniGame()
        {
            var manaFlip = FindFirstObjectByType<ManaFlipView>(FindObjectsInactive.Include);
            if (manaFlip != null) manaFlip.Open();
            else ShowToast("No mini game in this scene");
        }

        void OnEnable()
        {
            if (game == null) return;
            game.LevelStarted += OnLevelStarted;
            game.MovesChanged += OnMovesChanged;
            game.AddTubeChanged += OnAddTubeChanged;
            game.SmallTubeChanged += OnSmallTubeChanged;
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
            game.SmallTubeChanged -= OnSmallTubeChanged;
            game.LevelWon -= OnLevelWon;
            game.NoMoves -= OnNoMoves;
            game.Toast -= ShowToast;
        }

        static void Hook(Button b, UnityEngine.Events.UnityAction a)
        {
            if (b == null) return;
            Haptic.Bind(b);
            b.onClick.AddListener(a);
        }

        static void Show(GameObject go, bool on)
        {
            if (go != null) go.SetActive(on);
        }

        // between the winning move and the win popup: a restart would drop the rewards and a page opened now would sit under the popup
        bool WinPending => game != null && game.IsGameOver && !(successView != null && successView.activeSelf);

        public void OpenSettings() => SetSettings(true);

        void SetSettings(bool on)
        {
            if (on && WinPending) return;
            Show(settingsPanel, on);
            // moved last so it draws above every page, popups included, whatever the scene order
            if (on && settingsPanel != null)
            {
                settingsPanel.transform.SetAsLastSibling();
                if (toast != null && toast.transform.parent == settingsPanel.transform.parent) toast.transform.SetAsLastSibling();   // toasts stay on top
            }
            // the bars hide while the settings page is up
            foreach (string bar in new[] { "top_bar", "booster_bar" })
            {
                var t = transform.Find(bar);
                if (t != null && (on || mainMenu == null || !mainMenu.gameObject.activeSelf)) t.gameObject.SetActive(!on);
            }
            if (game != null) game.IsPaused = on;
        }

        void OnLevelStarted(int level, LevelData data)
        {
            if (levelText != null) levelText.text = $"LEVEL {level}";
            Show(successView, false);
            Show(noMoveView, false);
            Show(boosterInfoPanel, false);
        }

        void OnMovesChanged(int moves, int undosLeft)
        {
            if (undoCountText != null) undoCountText.text = undosLeft.ToString();
            if (undoButton != null) undoButton.interactable = undosLeft > 0 && moves > 0;
        }

        void OnSmallTubeChanged(int left)
        {
            if (smallTubeCountText != null) smallTubeCountText.text = left.ToString();
            if (smallTubeButton != null) smallTubeButton.interactable = left > 0;
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
            if (noMoveContinueButton != null) noMoveContinueButton.interactable = Wallet.Coins >= continueCost;
            Show(noMoveView, true);
        }

        void ContinueForCoins()
        {
            if (Wallet.Coins < continueCost)
            {
                ShowToast("Not enough coins");
                return;
            }
            if (!Wallet.TrySpend(continueCost)) return;
            Show(noMoveView, false);
            if (!game.Rewind(continueRewindMoves)) Wallet.Add(continueCost);   // nothing to rewind: refund
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
