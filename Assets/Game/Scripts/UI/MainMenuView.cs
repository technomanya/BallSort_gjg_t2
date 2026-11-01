using System.Collections;
using BallSortGame.Core;
using BallSortGame.Gameplay;
using BallSortGame.Haptics;
using BallSortGame.MiniGame;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace BallSortGame.UI
{
    /// <summary>
    /// Main menu over the dimmed board, wired like <see cref="GameHUD"/>: logo, coin balance, the MINI GAME button
    /// and a tap-anywhere START (the whole backdrop is the button). Shown when the game opens and from the Home
    /// buttons; the game is paused while it is up. Built by BallSort > Rebuild HUD (MainMenuBuilder).
    /// </summary>
    public class MainMenuView : MonoBehaviour
    {
        public GameManager game;
        [Tooltip("Show the menu when the scene starts.")]
        public bool showOnStart = true;

        [Header("Widgets")]
        public Button startButton;
        [Tooltip("Opens the mini game (Mana Flip, or Jump Path when the scene has no Mana Flip).")]
        [FormerlySerializedAs("jumpPathButton")] public Button miniGameButton;
        public Text coinsText;
        public Text levelText;
        [Tooltip("Game view parts (HUD bars) switched off while the menu is up; the board (GameManager.tubesRoot) is hidden too.")]
        public GameObject[] hideWhileOpen;

        [Header("Motion")]
        public RectTransform logo;
        public float logoBob = 18f;
        [Tooltip("Scaled in and out (the tap hint); the START button when empty.")]
        public RectTransform pulse;
        public RectTransform[] sparkles;

        static MainMenuView _open;

        /// <summary>True while a main menu is on screen.</summary>
        public static bool IsOpen => _open != null;

        Vector2 _logoHome;

        void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (logo != null) _logoHome = logo.anchoredPosition;
            Haptic.Bind(startButton);
            Haptic.Bind(miniGameButton);
            if (startButton != null) startButton.onClick.AddListener(Play);
            if (miniGameButton != null) miniGameButton.onClick.AddListener(OpenMiniGame);
            if (!showOnStart) gameObject.SetActive(false);
        }

        void OnEnable()
        {
            _open = this;
            if (game != null) game.IsPaused = true;
            ShowGameView(false);
            Wallet.Changed += OnCoinsChanged;
            if (game != null) game.LevelStarted += OnLevelStarted;
            StartCoroutine(RefreshNextFrame());
        }

        void OnDisable()
        {
            if (_open == this) _open = null;
            Wallet.Changed -= OnCoinsChanged;
            if (game != null) game.LevelStarted -= OnLevelStarted;
            if (logo != null) logo.anchoredPosition = _logoHome;
            ShowGameView(true);
        }

        /// <summary>The game view (HUD bars and the board) is closed while the menu is up and comes back when it leaves.</summary>
        void ShowGameView(bool on)
        {
            if (hideWhileOpen != null)
                foreach (var go in hideWhileOpen) if (go != null) go.SetActive(on);
            if (game != null && game.tubesRoot != null) game.tubesRoot.gameObject.SetActive(on);
        }

        void Update()
        {
            float t = Time.unscaledTime;
            if (logo != null) logo.anchoredPosition = _logoHome + new Vector2(0f, logoBob * Mathf.Sin(t * 1.6f));
            var pulsed = pulse != null ? pulse : startButton != null ? (RectTransform)startButton.transform : null;
            if (pulsed != null)
            {
                float k = 1f + 0.04f * Mathf.Sin(t * 4f);
                pulsed.localScale = new Vector3(k, k, 1f);
            }
            if (sparkles == null) return;
            for (int i = 0; i < sparkles.Length; i++)
            {
                if (sparkles[i] == null) continue;
                float a = 0.5f + 0.5f * Mathf.Sin(t * (1.3f + 0.4f * i) + i * 1.7f);
                sparkles[i].localScale = Vector3.one * (0.6f + 0.4f * a);
                if (sparkles[i].TryGetComponent(out Graphic g)) g.color = new Color(g.color.r, g.color.g, g.color.b, a);
            }
        }

        public void Open() => gameObject.SetActive(true);

        public void Play()
        {
            gameObject.SetActive(false);
            if (game != null) game.IsPaused = false;
        }

        /// <summary>The mini game opens over the menu; closing it comes back to the menu (still paused).</summary>
        public void OpenMiniGame()
        {
            var manaFlip = FindFirstObjectByType<ManaFlipView>(FindObjectsInactive.Include);
            if (manaFlip != null) { manaFlip.Open(); return; }
            var jumpPath = FindFirstObjectByType<JumpPathView>(FindObjectsInactive.Include);
            if (jumpPath != null) jumpPath.Open();
            else Debug.LogWarning("MainMenuView: no mini game in this scene.");
        }

        // the wallet gets its starting coins in JumpPathView.Start, which may run after ours
        IEnumerator RefreshNextFrame()
        {
            Refresh();
            yield return null;
            Refresh();
            ShowGameView(false);   // tubes built by GameManager.Start after our OnEnable
        }

        void Refresh()
        {
            OnCoinsChanged(Wallet.Coins);
            if (game != null) OnLevelStarted(game.CurrentLevel, null);
        }

        void OnCoinsChanged(int coins)
        {
            if (coinsText != null) coinsText.text = coins.ToString();
        }

        void OnLevelStarted(int level, LevelData data)
        {
            if (game != null) game.IsPaused = true;   // starting a level unpauses; keep the board still behind the menu
            WordLabel.Show(levelText, $"LEVEL {Mathf.Max(1, level)}", Mathf.Max(1, level));
        }
    }
}
