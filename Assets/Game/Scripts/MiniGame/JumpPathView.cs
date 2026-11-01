using System.Collections;
using System.Collections.Generic;
using BallSortGame.Gameplay;
using BallSortGame.LevelEditor;
using BallSortGame.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BallSortGame.MiniGame
{
    /// <summary>
    /// Jump Path mini game UI. Like the in-app level editor it attaches itself at runtime to any scene with a
    /// <see cref="GameManager"/> and builds its uGUI in code, so the hand-edited game scene is never touched.
    ///
    /// The panel opens from the main menu's JUMP PATH button (levels earn the coins: <see cref="LevelCoinsView"/>).
    /// It shows the current board as a winding path of stops from START to the prize; the Jump button spends coins and hops the token one stop. On the last stop the prize
    /// popup pays out and the next board (one jump longer) is laid out.
    /// Art comes from the NewUI kit through <see cref="UISkin"/>; without the skin asset it falls back to plain shapes.
    /// </summary>
    public class JumpPathView : MonoBehaviour
    {
        static readonly Color Gold = new Color(1f, 0.80f, 0.25f, 1f);
        static readonly Color Stop = new Color(0.26f, 0.26f, 0.36f, 1f);
        static readonly Color Dim = new Color(0.04f, 0.03f, 0.12f, 1f);
        static readonly Color Unreached = new Color(0.72f, 0.68f, 0.80f, 1f);
        static readonly Color TextShadow = new Color(0.1f, 0f, 0.2f, 0.8f);

        public GameManager game;
        public JumpPath rules = new JumpPath();

        [Header("Motion")]
        public float HopTime = 0.45f;
        public float HopHeight = 170f;

        // UI
        UISkin _skin;
        Canvas _canvas;
        CanvasScaler _scaler;
        GameObject _panel, _prizeView;
        RectTransform _path, _token;
        Text _panelCoinsText, _boardText, _prizeText, _flash;
        Button _jumpButton, _closeButton;
        readonly List<Image> _stops = new List<Image>();
        readonly List<Image> _rings = new List<Image>();   // gold frame shown on reached stops (skinned only)
        readonly List<Vector2> _anchors = new List<Vector2>();
        bool _hopping;
        int _shown;   // stop the token is drawn on; trails rules.Position while a hop plays
        Coroutine _flashRoutine;

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
            if (FindFirstObjectByType<JumpPathView>() != null) return;
            var game = FindFirstObjectByType<GameManager>();
            if (game == null) return;
            new GameObject("JumpPath").AddComponent<JumpPathView>().game = game;
        }

        void Start()
        {
            Wallet.Init(rules.StartingCoins);
            rules.Load();
            _skin = UISkin.Current;
            BuildUI();
            _panel.SetActive(false);
            _prizeView.SetActive(false);
            Wallet.Changed += OnCoinsChanged;
            OnCoinsChanged(Wallet.Coins);
        }

        void OnDestroy()
        {
            Wallet.Changed -= OnCoinsChanged;
        }

        void Update()
        {
            // keep the canvas at least 1080x2340 units in portrait and landscape windows
            if (_scaler != null) _scaler.matchWidthOrHeight = Screen.width * 2340f > Screen.height * 1080f ? 1f : 0f;
            if (_panel != null && _panel.activeSelf && !_hopping && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        // =====================================================================
        // Flow
        // =====================================================================

        public void Open()
        {
            _panel.SetActive(true);
            if (game != null) game.IsPaused = true;
            LayoutBoard();
            Refresh();
            if (rules.AtGoal) ShowPrize();
        }

        public void Close()
        {
            if (_hopping) return;
            _panel.SetActive(false);
            _prizeView.SetActive(false);
            if (game != null) game.IsPaused = MainMenuView.IsOpen;   // opened from the menu: back to the menu, still paused
        }

        void Jump()
        {
            if (_hopping || rules.AtGoal) return;
            int from = rules.Position;
            _hopping = true;
            if (!rules.TryJump())
            {
                _hopping = false;
                Flash($"You need {rules.JumpCost} coins - win levels to earn more");
                return;
            }
            StartCoroutine(HopRoutine(from, rules.Position));
        }

        void Collect()
        {
            int prize = rules.CollectPrize();
            _prizeView.SetActive(false);
            LayoutBoard();
            Refresh();
            Flash($"+{prize} coins!  Board {rules.Board + 1}: {rules.JumpsRequired} jumps");
        }

        void OnCoinsChanged(int coins)
        {
            if (_panelCoinsText != null) _panelCoinsText.text = coins.ToString();
            if (_panel != null && _panel.activeSelf) Refresh();
        }

        void Refresh()
        {
            _boardText.text = $"Board {rules.Board + 1}   |   {_shown} / {rules.JumpsRequired} jumps";
            for (int i = 0; i < _stops.Count; i++)
            {
                bool goal = i == _stops.Count - 1;
                if (_skin == null) _stops[i].color = goal ? Gold : i <= _shown ? EditorUI.Go : Stop;
                else if (!goal)
                {
                    _stops[i].color = i <= _shown ? Color.white : Unreached;
                    if (_rings[i] != null) _rings[i].enabled = i <= _shown;
                }
            }
            bool canJump = !_hopping && !rules.AtGoal && rules.CanAffordJump;
            _jumpButton.interactable = canJump;
            EditorUI.SetLabel(_jumpButton, rules.AtGoal ? "Collect your prize!"
                : rules.CanAffordJump ? $"JUMP   ( {rules.JumpCost} coins )"
                : $"Need {rules.JumpCost} coins");
            _closeButton.interactable = !_hopping;
        }

        void ShowPrize()
        {
            _prizeText.text = $"+{rules.Prize} coins";
            _prizeView.SetActive(true);
        }

        IEnumerator HopRoutine(int from, int to)
        {
            Refresh();
            Vector2 a = _anchors[from], b = _anchors[to];
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / HopTime);
                float e = Mathf.SmoothStep(0f, 1f, t);
                PlaceToken(Vector2.Lerp(a, b, e), HopHeight * 4f * t * (1f - t));
                float stretch = 1f + 0.18f * Mathf.Sin(t * Mathf.PI);
                _token.localScale = new Vector3(1f / stretch, stretch, 1f);
                yield return null;
            }
            // landing squash
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.18f);
                float s = 0.22f * Mathf.Sin(t * Mathf.PI);
                _token.localScale = new Vector3(1f + s, 1f - s, 1f);
                yield return null;
            }
            _token.localScale = Vector3.one;
            _shown = to;
            _hopping = false;
            Refresh();
            OnCoinsChanged(Wallet.Coins);
            if (rules.AtGoal)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                ShowPrize();
            }
        }

        void PlaceToken(Vector2 anchor, float lift)
        {
            _token.anchorMin = _token.anchorMax = anchor;
            _token.anchoredPosition = new Vector2(0f, 80f + lift);
        }

        // =====================================================================
        // UI construction
        // =====================================================================

        static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Image Circle(string name, Transform parent, Color color, float size)
        {
            var img = EditorUI.Image(name, parent, EditorUI.Circle, color);
            img.raycastTarget = false;
            Place(img.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            return img;
        }

        /// <summary>Skin sprite at its own colours, else a plain circle in <paramref name="fallback"/>.</summary>
        static Image Art(string name, Transform parent, Sprite sprite, Color fallback, float size)
        {
            var img = Circle(name, parent, fallback, size);
            if (sprite == null) return img;
            img.sprite = sprite;
            img.color = Color.white;
            img.preserveAspect = true;
            return img;
        }

        static T Shadowed<T>(T text) where T : Text
        {
            text.gameObject.AddComponent<Shadow>().effectColor = TextShadow;
            return text;
        }

        /// <summary>Coin icon, drawn as a plain gold circle when there is no skin.</summary>
        Image Coin(string name, Transform parent, float size)
        {
            var sprite = _skin != null ? _skin.coin : null;
            var coin = Art(name, parent, sprite, Gold, size);
            if (sprite == null) EditorUI.Label(coin.transform, "$", Mathf.RoundToInt(size * 0.65f), TextAnchor.MiddleCenter, new Color(0.55f, 0.38f, 0.05f));
            return coin;
        }

        /// <summary>EditorUI button wearing a 9-sliced skin sprite; the label gets a shadow to read on the art.</summary>
        static Button SkinButton(Transform parent, string label, Sprite sprite, Color fallback, UnityEngine.Events.UnityAction onClick, int fontSize, float flex = 1f)
        {
            var b = EditorUI.Button(parent, label, fallback, onClick, fontSize, flex);
            if (sprite == null) return b;
            var img = (Image)b.targetGraphic;
            img.sprite = sprite;
            img.color = Color.white;
            img.type = Image.Type.Sliced;
            var colors = b.colors;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.6f, 1f);   // greyed but opaque, so nothing shows through
            b.colors = colors;
            Shadowed(b.GetComponentInChildren<Text>());
            return b;
        }

        /// <summary>Coin icon with the balance next to it.</summary>
        Text CoinPill(Transform parent, float flex)
        {
            var sprite = _skin != null ? _skin.pill : null;
            var pill = EditorUI.Image("coins", parent, sprite != null ? sprite : EditorUI.RoundRect, sprite != null ? Color.white : new Color(0f, 0f, 0f, 0.55f), true);
            EditorUI.Flex(pill, flex);
            var coin = Coin("coin", pill.transform, 84f);
            Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(64f, 0f), new Vector2(84f, 84f));
            var text = Shadowed(EditorUI.Label(pill.transform, "0", 56, TextAnchor.MiddleCenter, Color.white));
            text.rectTransform.offsetMin = new Vector2(110f, 0f);
            return text;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("JumpPathCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 400;   // above the game HUD, below the level editor
            _scaler = canvasGo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1080, 2340);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            if (FindFirstObjectByType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var root = (RectTransform)canvasGo.transform;

            // ---- panel (opened from the main menu)
            var panel = EditorUI.Image("JumpPathPanel", root, null, Dim);
            EditorUI.Stretch(panel.rectTransform, Vector2.zero, Vector2.zero);
            _panel = panel.gameObject;
            var p = panel.transform;

            var top = EditorUI.Band("top_bar", p, true, 60f, 120f, 40f);
            EditorUI.RowLayout(top.gameObject, 24f);
            _panelCoinsText = CoinPill(top, 1.2f);
            EditorUI.Label(EditorUI.Flex(EditorUI.Rect("title", top), 1.6f), "JUMP PATH", 72, TextAnchor.MiddleCenter, Gold);
            _closeButton = EditorUI.Button(top, "X", EditorUI.Danger, Close, 56, 0.45f);
            if (_skin != null && _skin.buttonCircle != null)
            {
                var bg = (Image)_closeButton.targetGraphic;
                bg.sprite = _skin.buttonCircle;
                bg.color = Color.white;
                bg.type = Image.Type.Simple;
                bg.preserveAspect = true;
                EditorUI.SetLabel(_closeButton, "");
                Art("icon", _closeButton.transform, _skin.iconClose, Color.white, 76f);
            }

            var info = EditorUI.Band("board_info", p, true, 210f, 80f, 40f);
            _boardText = EditorUI.Label(info, "", 44, TextAnchor.MiddleCenter, Color.white);

            _path = EditorUI.Stretch(EditorUI.Rect("path", p), new Vector2(140f, 560f), new Vector2(-140f, -520f));

            var bottom = EditorUI.Band("jump", p, false, 230f, 200f, 120f);
            EditorUI.RowLayout(bottom.gameObject, 0f);
            _jumpButton = SkinButton(bottom, "JUMP", _skin != null ? _skin.buttonRectGreen : null, EditorUI.Go, Jump, 64);

            var flashBg = EditorUI.Image("flash", root, EditorUI.RoundRect, new Color(0f, 0f, 0f, 0.75f), true);
            flashBg.raycastTarget = false;
            Place(flashBg.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 120f));
            _flash = EditorUI.Label(flashBg.transform, "", 46, TextAnchor.MiddleCenter, Gold);
            _flash.transform.parent.gameObject.SetActive(false);

            // ---- prize popup
            var prize = EditorUI.Image("PrizeView", p, null, new Color(0f, 0f, 0f, 0.6f));
            EditorUI.Stretch(prize.rectTransform, Vector2.zero, Vector2.zero);
            _prizeView = prize.gameObject;
            var windowSprite = _skin != null ? _skin.popupPanel : null;
            var window = EditorUI.Image("window", prize.transform, windowSprite != null ? windowSprite : EditorUI.RoundRect, windowSprite != null ? Color.white : EditorUI.Bar, true);
            Place(window.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 760f));
            var title = Place(EditorUI.Rect("title", window.transform), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(780f, 120f));
            Shadowed(EditorUI.Label(title, "YOU MADE IT!", 80, TextAnchor.MiddleCenter, Gold));
            var chest = Coin("prize_coin", window.transform, 220f);
            Place(chest.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(220f, 220f));
            var amount = Place(EditorUI.Rect("amount", window.transform), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(780f, 100f));
            _prizeText = Shadowed(EditorUI.Label(amount, "", 72, TextAnchor.MiddleCenter, Color.white));
            var collect = SkinButton(window.transform, "COLLECT", _skin != null ? _skin.buttonRectGreen : null, EditorUI.Go, Collect, 60);
            Place((RectTransform)collect.transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(560f, 150f));
        }

        /// <summary>Lays out START, one stop per jump and the prize as a winding path from bottom to top.</summary>
        void LayoutBoard()
        {
            for (int i = _path.childCount - 1; i >= 0; i--) Destroy(_path.GetChild(i).gameObject);
            _stops.Clear();
            _rings.Clear();
            _anchors.Clear();

            int n = rules.JumpsRequired;
            for (int i = 0; i <= n; i++)
                _anchors.Add(new Vector2(0.5f + 0.42f * Mathf.Sin(i * Mathf.PI / 2f), i / (float)n));

            // trail dots between stops
            for (int i = 0; i < n; i++)
            for (int d = 1; d <= 3; d++)
            {
                var dot = Circle("trail", _path, new Color(1f, 1f, 1f, 0.35f), 22f);
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = Vector2.Lerp(_anchors[i], _anchors[i + 1], d / 4f);
            }

            for (int i = 0; i <= n; i++)
            {
                bool goal = i == n;
                float size = goal ? 220f : 150f;
                Image stop, ring = null;
                if (_skin == null)
                {
                    stop = Circle(goal ? "prize" : "stop_" + i, _path, Stop, size);
                    string label = i == 0 ? "START" : goal ? $"+{rules.Prize}" : i.ToString();
                    EditorUI.Label(stop.transform, label, goal ? 60 : i == 0 ? 34 : 56, TextAnchor.MiddleCenter, goal ? new Color(0.45f, 0.28f, 0.02f) : Color.white);
                }
                else if (goal)
                {
                    // gold frame holding the prize coin
                    stop = Art("prize", _path, _skin.circleFrame, Gold, size);
                    Coin("coin", stop.transform, 150f);
                }
                else
                {
                    stop = Art("stop_" + i, _path, _skin.buttonCircle, Stop, size);
                    ring = Art("ring", stop.transform, _skin.circleFrame, Gold, size + 24f);
                    Shadowed(EditorUI.Label(stop.transform, i == 0 ? "START" : i.ToString(), i == 0 ? 34 : 60, TextAnchor.MiddleCenter, Color.white));
                }
                stop.rectTransform.anchorMin = stop.rectTransform.anchorMax = _anchors[i];
                if (goal)
                {
                    var tag = Place(EditorUI.Rect("prize_tag", stop.transform), new Vector2(0.5f, 1f), new Vector2(0f, 40f), new Vector2(360f, 70f));
                    Shadowed(EditorUI.Label(tag, _skin == null ? "PRIZE" : $"PRIZE  +{rules.Prize}", 50, TextAnchor.MiddleCenter, Gold));
                }
                _stops.Add(stop);
                _rings.Add(ring);
            }

            // player token drawn above the stops: green gem in a gold frame (plain: white rim around a purple ball)
            if (_skin == null)
            {
                _token = Circle("token", _path, Color.white, 104f).rectTransform;
                Circle("ball", _token, EditorUI.Accent, 84f);
            }
            else
            {
                _token = Art("token", _path, _skin.circleFrame, Color.white, 116f).rectTransform;
                Art("gem", _token, _skin.greenCircle, EditorUI.Go, 84f);
            }
            _shown = Mathf.Min(rules.Position, n);
            PlaceToken(_anchors[_shown], 0f);
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

        [ContextMenu("Reset Jump Path progress")]
        void DebugReset()
        {
            rules.ResetProgress();
            if (_panel != null && _panel.activeSelf) { _prizeView.SetActive(false); LayoutBoard(); Refresh(); }
            OnCoinsChanged(Wallet.Coins);
        }
    }
}
