using System;
using System.Collections;
using System.Collections.Generic;
using BallSortGame.Core;
using BallSortGame.Gameplay;
using BallSortGame.LevelEditor;
using BallSortGame.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BallSortGame.MiniGame
{
    /// <summary>
    /// Coins earned inside a level. Like <see cref="JumpPathView"/> it attaches itself at runtime to any scene with a
    /// <see cref="GameManager"/> and builds its uGUI in code, so the hand-edited game scene is never touched.
    ///
    /// A counter under the HUD's top bar, right side, starts every level at 0. Each tube filled with one colour bursts
    /// <see cref="CoinsPerTube"/> coins that fly into it. On a win the coins go to the <see cref="Wallet"/>: the counter
    /// glides to the middle of the win screen, the main balance appears top left, where the main menu shows it, and the
    /// counter ticks down to zero while the coins fly up to the balance. Restarting a level drops its unbanked coins.
    /// Both pills are copies of the main menu's coin pill; without a menu they fall back to a plain skinned pill.
    /// </summary>
    public class LevelCoinsView : MonoBehaviour
    {
        static readonly Color Gold = new Color(1f, 0.80f, 0.25f, 1f);

        public GameManager game;

        [Header("Economy")]
        [Tooltip("Coin sprites that burst from every tube filled with one colour.")]
        public int CoinsPerTube = 3;
        [Tooltip("Coins each burst sprite is worth, so a filled tube pays CoinsPerTube x CoinValue.")]
        public int CoinValue = 10;

        [Header("Layout")]
        [Tooltip("Counter centre from the top right corner, in canvas units (1080 x 2340); clear of the top bar and the no-moves close button.")]
        public Vector2 CounterPosition = new Vector2(-200f, -370f);
        [Tooltip("Counter centre on the win screen, from the screen centre, and its scale there.")]
        public Vector2 WinPosition = new Vector2(0f, -170f);   // between the coin ring and the CONTINUE button of the gold panel
        public float WinScale = 1.1f;
        public float CoinSize = 84f;

        [Header("Motion")]
        public float PopTime = 0.25f;
        public float FlyTime = 0.55f;
        public float MoveTime = 0.45f;
        [Tooltip("Coin sprites that fly to the balance at the win; the amount is split between them.")]
        public int MaxBankCoins = 12;
        public float BankTime = 1f;

        UISkin _skin;
        GameHUD _hud;
        Canvas _canvas;
        RectTransform _root, _counter, _counterCoin, _total, _totalCoin;
        Text _counterText, _totalText;
        readonly HashSet<int> _paidTubes = new HashSet<int>();   // a tube undone and filled again pays once
        readonly List<GameObject> _flying = new List<GameObject>();
        int _levelCoins;
        float _counterScale = 1f, _counterPunch, _totalPunch;

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
            if (FindFirstObjectByType<LevelCoinsView>() != null) return;
            var game = FindFirstObjectByType<GameManager>();
            if (game == null) return;
            new GameObject("LevelCoins").AddComponent<LevelCoinsView>().game = game;
        }

        void Start()
        {
            _skin = UISkin.Current;
            _hud = FindFirstObjectByType<GameHUD>();
            BuildUI();
            ResetLevel();   // the first level may have started before we subscribed
            if (game == null) return;
            game.LevelStarted += OnLevelStarted;
            game.TubeCompleted += OnTubeCompleted;
            game.LevelWon += OnLevelWon;
        }

        void OnDestroy()
        {
            if (game == null) return;
            game.LevelStarted -= OnLevelStarted;
            game.TubeCompleted -= OnTubeCompleted;
            game.LevelWon -= OnLevelWon;
        }

        void Update()
        {
            // the menu shows its own balance; the settings column sits under the counter
            bool settings = _hud != null && _hud.settingsPanel != null && _hud.settingsPanel.activeSelf;
            bool show = !MainMenuView.IsOpen && !settings;
            if (_canvas.enabled != show) _canvas.enabled = show;

            float dt = Time.unscaledDeltaTime * 6f;
            _counterPunch = Mathf.MoveTowards(_counterPunch, 0f, dt);
            _totalPunch = Mathf.MoveTowards(_totalPunch, 0f, dt);
            _counter.localScale = Vector3.one * (_counterScale * (1f + 0.15f * _counterPunch));
            _total.localScale = Vector3.one * (1f + 0.15f * _totalPunch);
        }

        // =====================================================================
        // Flow
        // =====================================================================

        void OnLevelStarted(int level, LevelData data) => ResetLevel();

        void ResetLevel()
        {
            StopAllCoroutines();
            foreach (var go in _flying) if (go != null) Destroy(go);
            _flying.Clear();
            _paidTubes.Clear();
            SetCounter(0);
            PlaceCounter(0f);
            _total.gameObject.SetActive(false);
        }

        /// <summary>Counter between its spot under the top bar (0) and the middle of the win screen (1).</summary>
        void PlaceCounter(float k)
        {
            _counter.anchorMin = _counter.anchorMax = Vector2.Lerp(Vector2.one, new Vector2(0.5f, 0.5f), k);
            _counter.anchoredPosition = Vector2.Lerp(CounterPosition, WinPosition, k);
            _counterScale = Mathf.Lerp(1f, WinScale, k);
        }

        void SetCounter(int coins)
        {
            _levelCoins = coins;
            _counterText.text = coins.ToString();
        }

        void OnTubeCompleted(Tube tube)
        {
            if (CoinsPerTube <= 0 || CoinValue <= 0 || Testing() || !_paidTubes.Add(tube.Index)) return;
            var cam = game.cam != null ? game.cam : Camera.main;
            if (cam == null)
            {
                SetCounter(_levelCoins + CoinsPerTube * CoinValue);
                return;
            }
            // fan the coins out above the tube, then each swoops into the counter
            Vector3 from = cam.WorldToScreenPoint(tube.transform.position);
            float unit = _root.lossyScale.x;
            float step = CoinsPerTube > 1 ? Mathf.Min(40f, 160f / (CoinsPerTube - 1)) : 0f;
            for (int i = 0; i < CoinsPerTube; i++)
            {
                float angle = (90f + (i - (CoinsPerTube - 1) / 2f) * step) * Mathf.Deg2Rad;
                Vector3 burst = from + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * (UnityEngine.Random.Range(130f, 180f) * unit);
                StartCoroutine(Fly(from, burst, PopTime, _counterCoin, -0.25f, i * 0.07f, () =>
                {
                    SetCounter(_levelCoins + CoinValue);
                    _counterPunch = 1f;
                }));
            }
        }

        void OnLevelWon(int level)
        {
            if (!Testing()) StartCoroutine(BankRoutine());
        }

        IEnumerator BankRoutine()
        {
            while (_flying.Count > 0) yield return null;   // coins of the last tube are still on their way
            int amount = _levelCoins;
            if (amount <= 0) yield break;
            int shown = Wallet.Coins;
            Wallet.Add(amount);   // saved now; the rest is only the show
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / MoveTime);
                PlaceCounter(Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            _totalText.text = shown.ToString();
            _total.gameObject.SetActive(true);
            _totalPunch = 1f;
            yield return new WaitForSecondsRealtime(0.3f);

            int sprites = Mathf.Clamp(amount, 1, Mathf.Max(1, MaxBankCoins));
            float gap = BankTime / sprites;
            for (int i = 0; i < sprites; i++)
            {
                int share = amount * (i + 1) / sprites - amount * i / sprites;
                SetCounter(_levelCoins - share);
                _counterPunch = 1f;
                StartCoroutine(Fly(_counterCoin.position, _counterCoin.position, 0f, _totalCoin, 0.25f, 0f, () =>
                {
                    shown += share;
                    _totalText.text = shown.ToString();
                    _totalPunch = 1f;
                }));
                yield return new WaitForSecondsRealtime(gap);
            }
        }

        /// <summary>
        /// One coin sprite: pops from <paramref name="from"/> out to <paramref name="burst"/> (skipped when
        /// <paramref name="popTime"/> is 0), then curves into <paramref name="target"/>. <paramref name="bend"/> bows
        /// the path to the right of the flight direction (negative: to the left). Positions are screen pixels.
        /// </summary>
        IEnumerator Fly(Vector3 from, Vector3 burst, float popTime, RectTransform target, float bend, float delay, Action onLand)
        {
            var coin = Coin("flying_coin", _root);
            var rt = coin.rectTransform;
            _flying.Add(coin.gameObject);
            rt.position = from;
            rt.localScale = popTime > 0f ? Vector3.zero : Vector3.one;
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            for (float t = 0f; popTime > 0f && t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / popTime);
                float e = 1f - (1f - t) * (1f - t);
                rt.position = Vector3.LerpUnclamped(from, burst, e);
                rt.localScale = Vector3.one * e;
                yield return null;
            }

            Vector3 start = rt.position;
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / FlyTime);
                Vector3 end = target.position;
                Vector3 d = end - start;
                Vector3 ctrl = (start + end) * 0.5f + new Vector3(d.y, -d.x, 0f) * bend;
                float e = t * t;
                rt.position = Vector3.Lerp(Vector3.Lerp(start, ctrl, e), Vector3.Lerp(ctrl, end, e), e);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.8f, e);
                yield return null;
            }

            _flying.Remove(coin.gameObject);
            Destroy(coin.gameObject);
            onLand?.Invoke();
        }

        static bool Testing()
        {
            var editor = FindFirstObjectByType<LevelEditorManager>();
            return editor != null && editor.IsTesting;   // designer test plays earn nothing
        }

        // =====================================================================
        // UI construction
        // =====================================================================

        void BuildUI()
        {
            var canvasGo = new GameObject("LevelCoinsCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 390;   // over the HUD and its popups, under Jump Path and the level editor
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            var hudScaler = _hud != null ? _hud.GetComponentInParent<CanvasScaler>() : null;
            if (hudScaler != null)
            {
                // same scaling as the HUD, so the counter lines up with its top bar
                scaler.referenceResolution = hudScaler.referenceResolution;
                scaler.matchWidthOrHeight = hudScaler.matchWidthOrHeight;
            }
            _root = (RectTransform)canvasGo.transform;

            _counter = Pill("level_coins", out _counterText, out _counterCoin);
            _counter.pivot = new Vector2(0.5f, 0.5f);
            PlaceCounter(0f);

            _total = Pill("total_coins", out _totalText, out _totalCoin);   // keeps the main menu's spot, top left
        }

        /// <summary>Copy of the main menu's coin pill at its menu position, else a plain pill top left.</summary>
        RectTransform Pill(string name, out Text text, out RectTransform coin)
        {
            var source = MenuCoinPill();
            RectTransform pill;
            if (source != null)
            {
                pill = (RectTransform)Instantiate(source.gameObject, _root, false).transform;
                pill.gameObject.SetActive(true);
                text = pill.GetComponentInChildren<Text>(true);
                coin = pill.Find("coin") as RectTransform;
            }
            else
            {
                var sprite = _skin != null ? _skin.pill : null;
                var img = EditorUI.Image(name, _root, sprite != null ? sprite : EditorUI.RoundRect, sprite != null ? Color.white : new Color(0f, 0f, 0f, 0.55f), true);
                pill = img.rectTransform;
                pill.anchorMin = pill.anchorMax = new Vector2(0f, 1f);
                pill.anchoredPosition = new Vector2(230f, -130f);
                pill.sizeDelta = new Vector2(360f, 120f);
                coin = Coin("coin", pill).rectTransform;
                coin.anchorMin = coin.anchorMax = new Vector2(0f, 0.5f);
                coin.anchoredPosition = new Vector2(64f, 0f);
                text = EditorUI.Label(pill, "0", 56, TextAnchor.MiddleCenter, Color.white);
                text.rectTransform.offsetMin = new Vector2(110f, 0f);
            }
            pill.name = name;
            foreach (var g in pill.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            if (coin == null) coin = pill;
            return pill;
        }

        /// <summary>The main menu child holding its coin text, or null.</summary>
        RectTransform MenuCoinPill()
        {
            var menu = _hud != null && _hud.mainMenu != null ? _hud.mainMenu : FindFirstObjectByType<MainMenuView>(FindObjectsInactive.Include);
            if (menu == null || menu.coinsText == null) return null;
            var t = menu.coinsText.transform;
            while (t.parent != null && t.parent != menu.transform) t = t.parent;
            return t.parent == menu.transform ? t as RectTransform : null;
        }

        /// <summary>Coin icon, drawn as a plain gold circle when there is no skin.</summary>
        Image Coin(string name, Transform parent)
        {
            var sprite = _skin != null ? _skin.coin : null;
            var img = EditorUI.Image(name, parent, sprite != null ? sprite : EditorUI.Circle, sprite != null ? Color.white : Gold);
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(CoinSize, CoinSize);
            return img;
        }

        // ---- debug helper (component context menu)
        [ContextMenu("Add 10 level coins")]
        void DebugAddCoins() => SetCounter(_levelCoins + 10);
    }
}
