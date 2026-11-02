using System.Collections;
using BallSortGame.Gameplay;
using BallSortGame.Haptics;
using BallSortGame.LevelEditor;
using BallSortGame.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.MiniGame
{
    /// <summary>
    /// Mana Flip page: reward rows, a 3x4 card grid and the stage progress. The page is laid out in the scene.
    /// It opens by itself after the win that earns a play, and from the main menu. Rules live in <see cref="ManaFlip"/>.
    /// </summary>
    public class ManaFlipView : MonoBehaviour
    {
        static readonly Color Gold = new Color(1f, 0.72f, 0.12f, 1f);
        static readonly string[] ItemNames = { "potion", "crystal", "star" };

        public GameManager game;
        public ManaFlip rules = new ManaFlip();

        [Header("Motion")]
        public float FlipTime = 0.28f;
        [Tooltip("Seconds a matched board stays before the redeal.")]
        public float RoundEndDelay = 1.8f;
        [Tooltip("Delay after the earning win, so the coins bank first.")]
        public float OpenDelay = 2.2f;

        [Header("Scene widgets (wired by hand in the scene)")]
        [Tooltip("The full-screen page, inactive while closed.")]
        public GameObject panel;
        public Button settingsButton;
        public Button closeButton;
        [Tooltip("Card buttons in board order (3 columns x 4 rows).")]
        public Button[] cards = new Button[0];
        [Tooltip("Per card: tile, glow behind a found item, item picture.")]
        public Image[] backs = new Image[0];
        public Image[] glows = new Image[0];
        public Image[] faces = new Image[0];
        [Tooltip("Reward row check marks: row * copies + copy.")]
        public Image[] checks = new Image[0];
        [Tooltip("Coin prize label of each reward row.")]
        public Text[] prices = new Text[0];
        public Text coinsText;
        public Text stageText;
        public Text statusText;
        public Text flashText;

        UISkin _skin;
        GameHUD _hud;
        bool _busy;
        Coroutine _flashRoutine, _openRoutine;

        void Start()
        {
            rules.Load();
            _skin = UISkin.Current;
            _hud = FindFirstObjectByType<GameHUD>();
            Wire();
            panel.SetActive(false);
            Wallet.Changed += OnCoinsChanged;
            OnCoinsChanged(Wallet.Coins);
            if (game != null) game.LevelWon += OnLevelWon;
        }

        void OnDestroy()
        {
            Wallet.Changed -= OnCoinsChanged;
            if (game != null) game.LevelWon -= OnLevelWon;
        }

        void Update()
        {
            if (panel != null && panel.activeSelf && !_busy && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        public bool IsOpen => panel != null && panel.activeSelf;

        public void Open()
        {
            panel.SetActive(true);
            if (game != null) game.IsPaused = true;
            Refresh();
        }

        public void Close()
        {
            if (_busy) return;
            panel.SetActive(false);
            // the banner is a sibling of the panel, not a child: it would stay up over the game
            if (_flashRoutine != null) { StopCoroutine(_flashRoutine); _flashRoutine = null; }
            if (flashText != null) flashText.transform.parent.gameObject.SetActive(false);
            if (game != null) game.IsPaused = MainMenuView.IsOpen;   // opened from the menu: stay paused under it
        }

        // counts the win towards the next play and opens the board once earned
        void OnLevelWon(int level)
        {
            if (game == null || game.IsBotPlaying || Testing()) return;   // test wins must not touch the streak
            bool earned = rules.OnLevelWon(game.IsOneShotPlay);
            if (IsOpen) Refresh();
            if (!earned) return;
            if (_openRoutine != null) StopCoroutine(_openRoutine);
            _openRoutine = StartCoroutine(OpenAfter(OpenDelay));
        }

        bool Testing()
        {
            if (game.IsTestLevelOn || game.OverrideLevel != null) return true;
            var editor = FindFirstObjectByType<LevelEditorManager>();
            return editor != null && editor.IsTesting;
        }

        IEnumerator OpenAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _openRoutine = null;
            if (!rules.CanPlay || IsOpen) yield break;
            Open();
            Flash("You earned a play!  Flip the cards");
        }

        // closes first because the page draws above the HUD's settings
        void OpenSettings()
        {
            if (_busy) return;
            Close();
            if (_hud != null) _hud.OpenSettings();
        }

        void Flip(int card)
        {
            if (_busy || rules.IsRevealed(card)) return;
            var result = rules.TryFlip(card, out int item, out int reward);
            if (result == ManaFlip.FlipResult.Refused)
            {
                Flash(rules.CanPlay ? "Round over" : LockedMessage());
                return;
            }
            Haptic.Play(HapticType.ButtonClick);
            StartCoroutine(FlipRoutine(card, result, item, reward));
        }

        IEnumerator FlipRoutine(int card, ManaFlip.FlipResult result, int item, int reward)
        {
            _busy = true;
            yield return Turn(card, true);
            RefreshRows();
            if (result == ManaFlip.FlipResult.Reward)
            {
                Flash($"{ItemNames[item]} x{rules.MatchCount}:  +{reward} coins!");
                Haptic.Play(HapticType.Reward);
                // every open copy of the matched item pops, the one just turned last
                for (int i = 0; i < cards.Length; i++)
                    if (i != card && rules.IsRevealed(i) && rules.Cards[i] == item) StartCoroutine(Punch(faces[i].rectTransform));
                yield return Punch(faces[card].rectTransform);
                yield return new WaitForSecondsRealtime(RoundEndDelay);
                yield return Deal();
            }
            _busy = false;
            Refresh();
        }

        string LockedMessage()
        {
            int n = rules.LevelsToGo;
            return n == 1 ? "Win 1 more level on the first try to play" : $"Win {n} more levels on the first try to play";
        }

        // turns every open card back and shows the new board
        IEnumerator Deal()
        {
            rules.NewBoard();
            RefreshRows();
            for (int i = 0; i < cards.Length; i++)
                if (faces[i].gameObject.activeSelf) StartCoroutine(Turn(i, false));
            yield return new WaitForSecondsRealtime(FlipTime + 0.05f);
        }

        IEnumerator Turn(int card, bool open)
        {
            var t = cards[card].transform;
            float half = FlipTime * 0.5f;
            for (float k = 0f; k < 1f;)
            {
                k = Mathf.Min(1f, k + Time.unscaledDeltaTime / half);
                t.localScale = new Vector3(1f - k, 1f, 1f);
                yield return null;
            }
            ShowFace(card, open);
            for (float k = 0f; k < 1f;)
            {
                k = Mathf.Min(1f, k + Time.unscaledDeltaTime / half);
                t.localScale = new Vector3(k, 1f, 1f);
                yield return null;
            }
            t.localScale = Vector3.one;
        }

        IEnumerator Punch(RectTransform rt)
        {
            for (float k = 0f; k < 1f;)
            {
                k = Mathf.Min(1f, k + Time.unscaledDeltaTime / 0.45f);
                rt.localScale = Vector3.one * (1f + 0.35f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        void ShowFace(int card, bool open)
        {
            var face = faces[card];
            face.gameObject.SetActive(open);
            // a found card gets the dark tile and glow, or just fades without those sprites
            var back = backs[card];
            Sprite found = _skin != null ? _skin.cardBackFound : null;
            Sprite closed = _skin != null ? _skin.buttonSquare : null;
            if (found != null && closed != null) { back.sprite = open ? found : closed; back.color = Color.white; }
            else back.color = open ? new Color(1f, 1f, 1f, 0.6f) : Color.white;
            if (card < glows.Length && glows[card] != null) glows[card].gameObject.SetActive(open && glows[card].sprite != null);
            if (!open) return;
            int value = rules.Cards[card];
            face.sprite = ItemSprite(value);
            face.color = face.sprite != null ? Color.white : Gold;
        }

        void OnCoinsChanged(int coins)
        {
            if (coinsText != null) coinsText.text = coins.ToString();
        }

        void Refresh()
        {
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].transform.localScale = Vector3.one;
                ShowFace(i, rules.IsRevealed(i));
                cards[i].interactable = rules.CanPlay && !rules.IsRevealed(i) && !rules.RoundOver;
            }
            RefreshRows();
            OnCoinsChanged(Wallet.Coins);
            if (stageText != null) stageText.text = $"Stage {rules.StageIndex + 1}/{rules.StageCount}";
            if (statusText != null)
                statusText.text = rules.CanPlay
                    ? $"Flip the cards - first {rules.MatchCount} of a kind wins!"
                    : $"{LockedMessage()}  ({rules.Streak}/{rules.StageLevels})";
        }

        void RefreshRows()
        {
            int copies = rules.MatchCount;
            for (int item = 0; item < ManaFlip.ItemKinds; item++)
            {
                int found = rules.Found(item);
                for (int c = 0; c < copies; c++)
                {
                    int k = item * copies + c;
                    if (k < checks.Length && checks[k] != null) checks[k].gameObject.SetActive(c < found);
                }
                if (item < prices.Length && prices[item] != null) prices[item].text = rules.Reward(item).ToString();
            }
        }

        Sprite ItemSprite(int value)
        {
            if (_skin == null) return null;
            switch (value)
            {
                case 0: return _skin.rewardPotion;
                case 1: return _skin.rewardCrystal;
                case 2: return _skin.rewardStar;
                default: return null;
            }
        }

        // button listeners are not saved with the scene, so they are added here
        void Wire()
        {
            if (settingsButton != null) { settingsButton.onClick.AddListener(OpenSettings); Haptic.Bind(settingsButton); }
            if (closeButton != null) { closeButton.onClick.AddListener(Close); Haptic.Bind(closeButton); }
            for (int i = 0; i < cards.Length; i++)
            {
                int index = i;
                if (cards[i] != null) cards[i].onClick.AddListener(() => Flip(index));
            }
        }

        void Flash(string message)
        {
            if (flashText == null) return;
            flashText.text = message;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine());
        }

        IEnumerator FlashRoutine()
        {
            var bg = flashText.transform.parent.gameObject;
            bg.SetActive(true);
            yield return new WaitForSecondsRealtime(1.6f);
            bg.SetActive(false);
            _flashRoutine = null;
        }

        // ---- debug helpers (component context menu)
        [ContextMenu("Add 100 coins")]
        void DebugAddCoins() => Wallet.Add(100);

        [ContextMenu("Deal a new board")]
        void DebugDeal()
        {
            rules.NewBoard();
            if (IsOpen) Refresh();
        }

        [ContextMenu("Grant a play")]
        void DebugGrantPlay()
        {
            rules.GrantPlay();
            if (IsOpen) Refresh(); else Open();
        }

        [ContextMenu("Reset stage progress")]
        void DebugResetProgress()
        {
            rules.ResetProgress();
            if (IsOpen) Refresh();
        }
    }
}
