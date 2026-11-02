using System.Collections;
using BallSortGame.Core;
using BallSortGame.Garden;
using BallSortGame.Gameplay;
using BallSortGame.Haptics;
using BallSortGame.MiniGame;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.UI
{
    /// <summary>
    /// Main menu: the garden home page with its flower pots, coins, mini game and garden buttons, and a tap-anywhere
    /// START. The game is paused while it is up.
    /// </summary>
    public class MainMenuView : MonoBehaviour
    {
        /// <summary>A flower pot on the home page: its flower and the "+" that plants it.</summary>
        [System.Serializable]
        public class Pot
        {
            public RectTransform root;
            [Tooltip("Pivot at the stem foot. Hidden until planted.")]
            public Image flower;
            [Tooltip("The '+' on the empty pot, plants this slot.")]
            public Button addButton;
        }

        public GameManager game;
        [Tooltip("Show the menu when the scene starts.")]
        public bool showOnStart = true;

        [Header("Widgets")]
        public Button startButton;
        [Tooltip("Top left, under the coins: opens Mana Flip.")]
        public Button miniGameButton;
        [Tooltip("Closes a page left open over the menu.")]
        public Button homeButton;
        [Tooltip("Opens the Mana Garden. Made at runtime when empty.")]
        public Button gardenButton;
        [Tooltip("Top right: the HUD's settings page, opened over the menu.")]
        public Button settingsButton;
        public Text coinsText;
        public Text levelText;
        [Tooltip("HUD parts hidden while the menu is up.")]
        public GameObject[] hideWhileOpen;

        [Header("Garden")]
        [Tooltip("Pots in slot order: tulip, rose, lily, bellflower, daisy.")]
        public Pot[] pots;

        [Header("Motion")]
        public RectTransform logo;
        public float logoBob = 18f;
        [Tooltip("Pulsing tap hint. START button when empty.")]
        public RectTransform pulse;
        public RectTransform[] sparkles;

        static MainMenuView _open;

        public static bool IsOpen => _open != null;

        Vector2 _logoHome;
        GameHUD _hud;

        void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            _hud = GetComponentInParent<GameHUD>(true);
            if (logo != null) _logoHome = logo.anchoredPosition;
            SafeAreaFitter.Attach(transform.Find("safe"));   // the corner widgets keep clear of the cutout and gesture bar
            Haptic.Bind(startButton);
            Haptic.Bind(miniGameButton);
            Haptic.Bind(homeButton);
            Haptic.Bind(gardenButton);
            Haptic.Bind(settingsButton);
            if (startButton != null) startButton.onClick.AddListener(Play);
            if (miniGameButton != null) miniGameButton.onClick.AddListener(OpenMiniGame);
            if (homeButton != null) homeButton.onClick.AddListener(Home);
            if (gardenButton != null) gardenButton.onClick.AddListener(OpenGarden);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
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

        // HUD bars and the board
        void ShowGameView(bool on)
        {
            if (hideWhileOpen != null)
                foreach (var go in hideWhileOpen) if (go != null) go.SetActive(on);
            if (game != null && game.tubesRoot != null) game.tubesRoot.gameObject.SetActive(on);
        }

        void Update()
        {
            if (game != null && !game.IsPaused) game.IsPaused = true;   // closing settings unpauses, but the menu is still up
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

        public void OpenMiniGame()
        {
            var manaFlip = FindFirstObjectByType<ManaFlipView>(FindObjectsInactive.Include);
            if (manaFlip != null) manaFlip.Open();
            else Debug.LogWarning("MainMenuView: no mini game in this scene.");
        }

        /// <summary>The menu is the home screen, so Home only closes a page left open over it.</summary>
        public void Home()
        {
            var manaFlip = FindFirstObjectByType<ManaFlipView>(FindObjectsInactive.Include);
            if (manaFlip != null && manaFlip.IsOpen) manaFlip.Close();
            var garden = FindFirstObjectByType<ManaGardenView>(FindObjectsInactive.Include);
            if (garden != null && garden.IsOpen) garden.Close();
        }

        public void OpenGarden()
        {
            var garden = FindFirstObjectByType<ManaGardenView>(FindObjectsInactive.Include);
            if (garden != null) garden.Open();
            else Debug.LogWarning("MainMenuView: no Mana Garden in this scene.");
        }

        public void OpenSettings()
        {
            if (_hud != null) _hud.OpenSettings();
            else Debug.LogWarning("MainMenuView: no settings page in this scene.");
        }

        // GameManager.Start builds the tubes after our OnEnable, so refresh once more a frame later
        IEnumerator RefreshNextFrame()
        {
            Refresh();
            yield return null;
            Refresh();
            ShowGameView(false);
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
            if (game != null) game.IsPaused = true;   // starting a level unpauses, keep the board still behind the menu
            if (levelText != null) levelText.text = $"LEVEL {Mathf.Max(1, level)}";
        }
    }
}
