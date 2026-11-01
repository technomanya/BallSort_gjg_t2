using System.Collections;
using System.Collections.Generic;
using BallSortGame.Gameplay;
using BallSortGame.Haptics;
using BallSortGame.LevelEditor;
using BallSortGame.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BallSortGame.MiniGame
{
    /// <summary>
    /// Mana Flip mini game UI, after the mockup: header (settings / "Mana Flip" / close), the reward panel with a row
    /// per item (icons that get a check mark as copies are found, and the stage's coin prize), the 3x4 card grid and
    /// a footer with the balance and the stage progress. Like <see cref="JumpPathView"/> it attaches itself at runtime
    /// to any scene with a <see cref="GameManager"/> and builds its uGUI in code from <see cref="UISkin"/>, so the
    /// game scene is never touched. It opens by itself after the level win that earns a play (see
    /// <see cref="ManaFlip.OnLevelWon"/>) and from the main menu (<see cref="MainMenuView"/>), where it shows the
    /// progress towards the next play; the rules live in <see cref="ManaFlip"/>.
    /// </summary>
    public class ManaFlipView : MonoBehaviour
    {
        static readonly Color Gold = new Color(1f, 0.72f, 0.12f, 1f);
        static readonly Color Navy = new Color(0.12f, 0.06f, 0.30f, 1f);
        static readonly Color Dim = new Color(0.03f, 0.02f, 0.10f, 0.92f);
        static readonly Color Panel = new Color(0.62f, 0.15f, 0.48f, 1f);     // fallback without the skin
        static readonly Color Card = new Color(0.52f, 0.12f, 0.42f, 1f);
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly string[] ItemNames = { "potion", "crystal", "star" };

        public GameManager game;
        public ManaFlip rules = new ManaFlip();

        [Header("Motion")]
        public float FlipTime = 0.28f;
        [Tooltip("Seconds the matched board stays visible before it is dealt again.")]
        public float RoundEndDelay = 1.8f;
        [Tooltip("Seconds after the win that earns a play before the mini game opens, so the coins can bank first.")]
        public float OpenDelay = 2.2f;

        UISkin _skin;
        GameHUD _hud;
        Canvas _canvas;
        CanvasScaler _scaler;
        GameObject _panel;
        Text _coinsText, _stageText, _statusText, _flash;
        readonly Text[] _prices = new Text[ManaFlip.ItemKinds];
        readonly List<Button> _cards = new List<Button>();
        readonly List<Image> _faces = new List<Image>();
        readonly List<Image> _backs = new List<Image>();
        readonly List<Image> _glows = new List<Image>();
        readonly Image[][] _checks = new Image[ManaFlip.ItemKinds][];
        bool _busy;
        Coroutine _flashRoutine, _openRoutine;

        // =====================================================================
        // Bootstrap: attach to every gameplay scene
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
            if (FindFirstObjectByType<ManaFlipView>() != null) return;
            var game = FindFirstObjectByType<GameManager>();
            if (game == null) return;
            new GameObject("ManaFlip").AddComponent<ManaFlipView>().game = game;
        }

        void Start()
        {
            rules.Load();
            _skin = UISkin.Current;
            _hud = FindFirstObjectByType<GameHUD>();
            BuildUI();
            _panel.SetActive(false);
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
            if (_scaler != null) _scaler.matchWidthOrHeight = Screen.width * 2340f > Screen.height * 1080f ? 1f : 0f;
            if (_panel != null && _panel.activeSelf && !_busy && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        // =====================================================================
        // Flow
        // =====================================================================

        public bool IsOpen => _panel != null && _panel.activeSelf;

        public void Open()
        {
            _panel.SetActive(true);
            if (game != null) game.IsPaused = true;
            Refresh();
        }

        public void Close()
        {
            if (_busy) return;
            _panel.SetActive(false);
            if (game != null) game.IsPaused = MainMenuView.IsOpen;   // opened from the menu: back to the menu, still paused
        }

        /// <summary>Counts the win towards the next play and opens the board when it is earned.</summary>
        void OnLevelWon(int level)
        {
            if (game == null || game.IsBotPlaying) return;
            bool earned = rules.OnLevelWon(game.IsOneShotPlay);
            if (IsOpen) Refresh();
            if (!earned) return;
            if (_openRoutine != null) StopCoroutine(_openRoutine);
            _openRoutine = StartCoroutine(OpenAfter(OpenDelay));
        }

        IEnumerator OpenAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _openRoutine = null;
            if (!rules.CanPlay || IsOpen) yield break;
            Open();
            Flash("You earned a play!  Flip the cards");
        }

        /// <summary>The HUD's settings page; the mini game closes first because it is drawn above the HUD.</summary>
        void OpenSettings()
        {
            if (_busy) return;
            Close();
            if (_hud != null && _hud.settingsButton != null) _hud.settingsButton.onClick.Invoke();
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
                // every open copy of the matched item pops; the one just turned last
                for (int i = 0; i < _cards.Count; i++)
                    if (i != card && rules.IsRevealed(i) && rules.Cards[i] == item) StartCoroutine(Punch(_faces[i].rectTransform));
                yield return Punch(_faces[card].rectTransform);
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

        /// <summary>Turns every open card back, shuffles and shows the new board.</summary>
        IEnumerator Deal()
        {
            rules.NewBoard();
            RefreshRows();
            for (int i = 0; i < _cards.Count; i++)
                if (_faces[i].gameObject.activeSelf) StartCoroutine(Turn(i, false));
            yield return new WaitForSecondsRealtime(FlipTime + 0.05f);
        }

        IEnumerator Turn(int card, bool open)
        {
            var t = _cards[card].transform;
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
            var face = _faces[card];
            face.gameObject.SetActive(open);
            // a found card shows the kit's dark tile with the pink glow behind the item; without those sprites it just fades
            var back = _backs[card];
            Sprite found = _skin != null ? _skin.cardBackFound : null;
            Sprite closed = _skin != null ? _skin.buttonSquare : null;
            if (found != null && closed != null) { back.sprite = open ? found : closed; back.color = Color.white; }
            else back.color = open ? new Color(1f, 1f, 1f, 0.6f) : Color.white;
            if (card < _glows.Count && _glows[card] != null) _glows[card].gameObject.SetActive(open && _glows[card].sprite != null);
            if (!open) return;
            int value = rules.Cards[card];
            face.sprite = ItemSprite(value);
            face.color = face.sprite != null ? Color.white : Gold;
        }

        void OnCoinsChanged(int coins)
        {
            if (_coinsText != null) _coinsText.text = coins.ToString();
        }

        void Refresh()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].transform.localScale = Vector3.one;
                ShowFace(i, rules.IsRevealed(i));
                _cards[i].interactable = rules.CanPlay && !rules.IsRevealed(i) && !rules.RoundOver;
            }
            RefreshRows();
            OnCoinsChanged(Wallet.Coins);
            if (_stageText != null) _stageText.text = $"Stage {rules.StageIndex + 1}/{rules.StageCount}";
            if (_statusText != null)
                _statusText.text = rules.CanPlay
                    ? $"Flip the cards - first {rules.MatchCount} of a kind wins!"
                    : $"{LockedMessage()}  ({rules.Streak}/{rules.StageLevels})";
        }

        void RefreshRows()
        {
            for (int item = 0; item < ManaFlip.ItemKinds; item++)
            {
                int found = rules.Found(item);
                var checks = _checks[item];
                if (checks != null)
                    for (int c = 0; c < checks.Length; c++) if (checks[c] != null) checks[c].gameObject.SetActive(c < found);
                if (_prices[item] != null) _prices[item].text = rules.Reward(item).ToString();
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

        // =====================================================================
        // UI construction (1080 x 2340; mockup pixels x 1.078)
        // =====================================================================

        static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = Center;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>Skin sprite (9-sliced when asked), else a plain rounded shape in <paramref name="fallback"/>.</summary>
        static Image Img(string name, Transform parent, Sprite sprite, Color fallback, Vector2 pos, Vector2 size, bool sliced = false)
        {
            var img = Rect(name, parent, pos, size).gameObject.AddComponent<Image>();
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
                img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
                img.preserveAspect = !sliced;
                if (sliced) RoundEnds(img, size.y);
            }
            else
            {
                img.sprite = EditorUI.RoundRect;
                img.color = fallback;
                img.type = Image.Type.Sliced;
            }
            return img;
        }

        /// <summary>A sliced stadium sprite (borders of half its height: the gold pill, the reward strip) keeps round ends at any height.</summary>
        static void RoundEnds(Image img, float height)
        {
            var s = img.sprite;
            float ends = s.border.y + s.border.w;
            img.pixelsPerUnitMultiplier = ends >= s.rect.height * 0.9f && height > 0f ? ends / height : 1f;
        }

        static Text Label(string name, Transform parent, Vector2 pos, Vector2 size, string text, int fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var t = Rect(name, parent, pos, size).gameObject.AddComponent<Text>();
            t.font = EditorUI.Font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = WordArt.Style(FontStyle.Bold);
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.6f);
            s.effectDistance = new Vector2(0f, -5f);
            return t;
        }

        static Text Title(string name, Transform parent, Vector2 pos, Vector2 size, string text, int fontSize)
        {
            var t = Label(name, parent, pos, size, text, fontSize, Gold);
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = Navy;
            o.effectDistance = new Vector2(4f, -4f);
            return t;
        }

        Button SquareButton(string name, Transform parent, Vector2 pos, float size, Sprite icon, string fallbackLabel, UnityEngine.Events.UnityAction onClick)
        {
            var bg = Img(name, parent, _skin != null ? _skin.buttonSquare : null, Card, pos, new Vector2(size, size), true);
            var b = bg.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            Haptic.Bind(b);
            b.onClick.AddListener(onClick);
            if (icon != null) Img("icon", bg.transform, icon, Color.white, new Vector2(0f, 4f), new Vector2(size * 0.68f, size * 0.68f)).raycastTarget = false;
            else Label("label", bg.transform, Vector2.zero, Vector2.zero, fallbackLabel, Mathf.RoundToInt(size * 0.45f), Color.white);
            return b;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("ManaFlipCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 405;   // above the game HUD and the level coins, below the level editor
            _scaler = canvasGo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1080, 2340);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            if (FindFirstObjectByType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var root = (RectTransform)canvasGo.transform;

            // ---- full-screen panel over the board
            var panel = Rect("ManaFlipPanel", root, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            panel.rectTransform.anchorMin = Vector2.zero;
            panel.rectTransform.anchorMax = Vector2.one;
            panel.color = Dim;
            _panel = panel.gameObject;
            var p = panel.transform;

            Sprite pill = _skin != null ? _skin.pill : null;                                   // small gold-framed panel
            Sprite goldPill = _skin != null && _skin.buttonPill != null ? _skin.buttonPill : pill;   // gold stadium: prices, balance, flash
            Sprite bigPanel = _skin != null && _skin.popupPanel != null ? _skin.popupPanel : pill;
            Sprite rowStrip = _skin != null && _skin.rewardRow != null ? _skin.rewardRow : (_skin != null ? _skin.buttonRect : null);
            Sprite rect = _skin != null ? _skin.buttonRect : null;
            Sprite coinSprite = _skin != null ? _skin.coin : null;

            // ---- header: settings / title / close
            SquareButton("btn - settings", p, new Vector2(-411f, 625f), 140f, _skin != null ? _skin.iconSettings : null, "S", OpenSettings);
            SquareButton("btn - close", p, new Vector2(411f, 625f), 140f, _skin != null ? _skin.iconClose : null, "X", Close);
            var titleBg = Img("title_panel", p, rect, Panel, new Vector2(0f, 652f), new Vector2(440f, 180f), true);
            WordArt.Picture(Title("title", titleBg.transform, new Vector2(0f, 22f), new Vector2(420f, 100f), "Mana Flip", 66), _skin != null ? _skin.wordManaFlip : null);
            _stageText = Label("stage", titleBg.transform, new Vector2(0f, -40f), new Vector2(420f, 50f), "", 36, Color.white);

            // ---- reward panel: one row per item
            var rewards = Img("rewards", p, pill, Panel, new Vector2(0f, 372f), new Vector2(780f, 356f), true);
            for (int item = 0; item < ManaFlip.ItemKinds; item++)
            {
                float y = 108f - 108f * item;
                var row = Img("row_" + ItemNames[item], rewards.transform, rowStrip, Card, new Vector2(0f, y), new Vector2(700f, 92f), true);
                _checks[item] = new Image[rules.MatchCount];
                for (int c = 0; c < rules.MatchCount; c++)
                {
                    float x = -265f + 135f * c;
                    var icon = Img("item_" + c, row.transform, ItemSprite(item), Gold, new Vector2(x, 0f), new Vector2(76f, 76f));
                    icon.raycastTarget = false;
                    var check = Img("check", icon.transform, _skin != null ? _skin.iconCheck : null, Color.white, new Vector2(16f, 8f), new Vector2(70f, 70f));
                    check.raycastTarget = false;
                    check.gameObject.SetActive(false);
                    _checks[item][c] = check;
                }
                var price = Img("price", row.transform, goldPill, Navy, new Vector2(231f, 0f), new Vector2(200f, 78f), true);
                price.raycastTarget = false;
                Img("coin", price.transform, coinSprite, Gold, new Vector2(-62f, 0f), new Vector2(58f, 58f)).raycastTarget = false;
                _prices[item] = Label("amount", price.transform, new Vector2(24f, 2f), new Vector2(130f, 70f), rules.Reward(item).ToString(), 48, Color.white);
            }

            // ---- card grid: 3 columns x 4 rows
            // the big panel is drawn scaled (not sliced) so the sparkles on its sides keep their shape; its aspect already matches
            var grid = Img("grid", p, bigPanel, Panel, new Vector2(0f, -349f), bigPanel != null ? new Vector2(925f, 1040f) : new Vector2(776f, 873f), bigPanel == null);   // ~50 px transparent margin around the frame
            grid.preserveAspect = false;
            int cols = 3;
            int rows = Mathf.CeilToInt(rules.CardCount / (float)cols);
            float pitchX = 215f, pitchY = 210f;
            for (int i = 0; i < rules.CardCount; i++)
            {
                int col = i % cols, rowIdx = i / cols;
                var pos = new Vector2((col - (cols - 1) / 2f) * pitchX, ((rows - 1) / 2f - rowIdx) * pitchY);
                var back = Img("card_" + i, grid.transform, _skin != null ? _skin.buttonSquare : null, Card, pos, new Vector2(180f, 180f), true);
                var b = back.gameObject.AddComponent<Button>();
                b.targetGraphic = back;
                var colors = b.colors; colors.disabledColor = Color.white; b.colors = colors;   // open cards keep their look
                int index = i;
                b.onClick.AddListener(() => Flip(index));
                var glow = Img("glow", back.transform, _skin != null ? _skin.rewardGlow : null, Color.clear, new Vector2(0f, 2f), new Vector2(196f, 196f));
                glow.raycastTarget = false;
                glow.gameObject.SetActive(false);
                var face = Img("face", back.transform, null, Gold, new Vector2(0f, 2f), new Vector2(124f, 124f));
                face.raycastTarget = false;
                face.gameObject.SetActive(false);
                _cards.Add(b);
                _backs.Add(back);
                _glows.Add(glow);
                _faces.Add(face);
            }

            // ---- footer: balance and the play / progress line
            var balance = Img("coins", p, goldPill, Navy, new Vector2(0f, -860f), new Vector2(330f, 110f), true);
            Img("coin", balance.transform, coinSprite, Gold, new Vector2(-105f, 0f), new Vector2(84f, 84f)).raycastTarget = false;
            _coinsText = Label("text", balance.transform, new Vector2(40f, 2f), new Vector2(200f, 80f), "0", 52, Color.white);
            _statusText = Label("status", p, new Vector2(0f, -960f), new Vector2(980f, 70f), "", 38, Color.white);

            // ---- flash message
            var flashBg = Img("flash", root, goldPill, new Color(0f, 0f, 0f, 0.75f), new Vector2(0f, -720f), new Vector2(960f, 110f), true);
            flashBg.raycastTarget = false;
            _flash = Label("text", flashBg.transform, Vector2.zero, new Vector2(900f, 90f), "", 44, Gold);
            flashBg.gameObject.SetActive(false);
        }

        void Flash(string message)
        {
            _flash.text = message;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine());
        }

        IEnumerator FlashRoutine()
        {
            var bg = _flash.transform.parent.gameObject;
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
