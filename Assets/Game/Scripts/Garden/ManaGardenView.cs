using System.Collections;
using BallSortGame.Core;
using BallSortGame.Gameplay;
using BallSortGame.Haptics;
using BallSortGame.LevelEditor;
using BallSortGame.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BallSortGame.Garden
{
    /// <summary>
    /// Mana Garden page, after the "plant page" mockup: header (settings / "Mana Garden" / close), the gold-framed
    /// panel with a 3 + 2 grid of plant slots, each under a small gold "LEVEL n" pill, and the round plant button at
    /// the bottom with the green seed count. Like the mini games it attaches itself at runtime to any scene with a
    /// <see cref="GameManager"/> and builds its uGUI in code from <see cref="UISkin"/>; the scene is never touched.
    /// It opens from the main menu through a round plant button it adds to <see cref="MainMenuView"/> when the menu
    /// has none. On the win that unlocks a plant the HUD's win screen shows the seed in place of the coin. The rules
    /// live in <see cref="ManaGarden"/>.
    /// </summary>
    public class ManaGardenView : MonoBehaviour
    {
        static readonly Color Gold = new Color(1f, 0.72f, 0.12f, 1f);
        static readonly Color Navy = new Color(0.12f, 0.06f, 0.30f, 1f);
        static readonly Color Dim = new Color(0.03f, 0.02f, 0.10f, 0.92f);
        static readonly Color Panel = new Color(0.62f, 0.15f, 0.48f, 1f);     // fallback without the skin
        static readonly Color Card = new Color(0.52f, 0.12f, 0.42f, 1f);
        static readonly Color Green = new Color(0.25f, 0.85f, 0.45f, 1f);
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        /// <summary>Mockup pixel to canvas unit (the plant page mockup is 1003 px wide, the canvas 1080).</summary>
        const float Scale = 1080f / 1003f;

        public GameManager game;
        public ManaGarden rules = new ManaGarden();

        [Header("Layout")]
        [Tooltip("Round plant button on the main menu, from the top right corner of the menu (canvas units).")]
        public Vector2 MenuButtonPosition = new Vector2(-150f, -133f);
        public float MenuButtonSize = 140f;

        UISkin _skin;
        GameHUD _hud;
        Canvas _canvas;
        CanvasScaler _scaler;
        GameObject _panel;
        Image[] _slotArt;
        Image _badge, _menuBadge, _winCoin;
        Text _badgeText, _menuBadgeText;
        Sprite _winCoinSprite;
        Button _menuButton;

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
            if (FindFirstObjectByType<ManaGardenView>() != null) return;
            var game = FindFirstObjectByType<GameManager>();
            if (game == null) return;
            new GameObject("ManaGarden").AddComponent<ManaGardenView>().game = game;
        }

        void Start()
        {
            rules.Load();
            _skin = UISkin.Current;
            _hud = FindFirstObjectByType<GameHUD>();
            BuildUI();
            _panel.SetActive(false);
            AttachMenuButton();
            if (game == null) return;
            game.LevelWon += OnLevelWon;
            game.LevelStarted += OnLevelStarted;
        }

        void OnDestroy()
        {
            if (game == null) return;
            game.LevelWon -= OnLevelWon;
            game.LevelStarted -= OnLevelStarted;
        }

        void Update()
        {
            if (_scaler != null) _scaler.matchWidthOrHeight = Screen.width * 2340f > Screen.height * 1080f ? 1f : 0f;
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
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
            _panel.SetActive(false);
            if (game != null) game.IsPaused = MainMenuView.IsOpen;   // opened from the menu: back to the menu, still paused
        }

        /// <summary>The HUD's settings page; the garden closes first because it is drawn above the HUD.</summary>
        void OpenSettings()
        {
            Close();
            if (_hud != null && _hud.settingsButton != null) _hud.settingsButton.onClick.Invoke();
        }

        /// <summary>Plant button: the next seed in hand grows in its slot.</summary>
        void Plant()
        {
            int slot = rules.Plant();
            Refresh();
            if (slot < 0) return;
            Haptic.Play(HapticType.Reward);
            if (_slotArt != null && slot < _slotArt.Length && _slotArt[slot] != null) StartCoroutine(Punch(_slotArt[slot].rectTransform));
        }

        /// <summary>A win that unlocks a plant: the seed takes the coin's place on the win screen.</summary>
        void OnLevelWon(int level)
        {
            if (game == null || game.IsBotPlaying || Testing()) return;
            if (!rules.OnLevelWon(level)) return;
            RefreshBadges();
            ShowSeedOnWinScreen();
        }

        void OnLevelStarted(int level, LevelData data) => RestoreWinScreen();

        void ShowSeedOnWinScreen()
        {
            if (_skin == null || _skin.rewardSeed == null || _hud == null || _hud.successView == null) return;
            var coin = FindDeep(_hud.successView.transform, "coin");
            if (coin == null || !coin.TryGetComponent(out Image img)) return;
            if (_winCoin == null) { _winCoin = img; _winCoinSprite = img.sprite; }
            img.sprite = _skin.rewardSeed;
            StartCoroutine(Punch(img.rectTransform));
        }

        void RestoreWinScreen()
        {
            if (_winCoin == null) return;
            _winCoin.sprite = _winCoinSprite;
            _winCoin.rectTransform.localScale = Vector3.one;
            _winCoin = null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var hit = FindDeep(c, name);
                if (hit != null) return hit;
            }
            return null;
        }

        static bool Testing()
        {
            var editor = FindFirstObjectByType<LevelEditorManager>();
            return editor != null && editor.IsTesting;   // designer test plays earn nothing
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

        void Refresh()
        {
            if (_slotArt != null)
                for (int i = 0; i < _slotArt.Length; i++)
                {
                    var art = _slotArt[i];
                    if (art == null) continue;
                    bool planted = rules.IsPlanted(i), unlocked = rules.IsUnlocked(i);
                    art.gameObject.SetActive(unlocked);
                    if (!unlocked) continue;
                    // the seed waits in the slot until it is planted; then the plant grows
                    art.sprite = planted ? (_skin != null ? _skin.iconPlant : null) : (_skin != null ? _skin.rewardSeed : null);
                    art.color = art.sprite != null ? Color.white : Green;
                    float size = planted ? 150f : 100f;
                    art.rectTransform.sizeDelta = new Vector2(size, size);
                    art.rectTransform.localScale = Vector3.one;
                }
            RefreshBadges();
        }

        void RefreshBadges()
        {
            int seeds = rules.Seeds;
            SetBadge(_badge, _badgeText, seeds);
            SetBadge(_menuBadge, _menuBadgeText, seeds);
        }

        static void SetBadge(Image badge, Text text, int count)
        {
            if (badge == null) return;
            badge.gameObject.SetActive(count > 0);
            if (text != null) text.text = count.ToString();
        }

        // =====================================================================
        // Main menu button
        // =====================================================================

        /// <summary>The menu's plant button: the tab-bar one when the menu has it (badge added here), else one made here, top right.</summary>
        void AttachMenuButton()
        {
            var menu = FindFirstObjectByType<MainMenuView>(FindObjectsInactive.Include);
            if (menu == null) return;
            if (menu.gardenButton == null)
            {
                var rt = Rect("btn - mana_garden", menu.transform, MenuButtonPosition, new Vector2(MenuButtonSize, MenuButtonSize));
                rt.anchorMin = rt.anchorMax = Vector2.one;
                var bg = rt.gameObject.AddComponent<Image>();
                Skin(bg, _skin != null ? _skin.buttonCircle : null, Card, false);
                var b = bg.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                Img("icon", bg.transform, _skin != null ? _skin.iconPlant : null, Green, new Vector2(0f, 4f), new Vector2(MenuButtonSize * 0.62f, MenuButtonSize * 0.62f)).raycastTarget = false;
                _menuBadge = Badge(bg.transform, new Vector2(MenuButtonSize * 0.36f, MenuButtonSize * 0.4f), 64f, out _menuBadgeText);
                menu.gardenButton = b;
            }
            else if (menu.gardenButton.transform.Find("badge") == null)
            {
                // the menu's own tab-bar button (MainMenuBuilder): the seed badge goes on its top right corner
                float size = ((RectTransform)menu.gardenButton.transform).sizeDelta.x;
                _menuBadge = Badge(menu.gardenButton.transform, new Vector2(size * 0.36f, size * 0.4f), 64f, out _menuBadgeText);
            }
            _menuButton = menu.gardenButton;
            _menuButton.onClick.RemoveListener(Open);
            _menuButton.onClick.AddListener(Open);
            Haptic.Bind(_menuButton);
            RefreshBadges();
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

        /// <summary>Skin sprite (9-sliced when asked, round ends kept), else a plain rounded shape in <paramref name="fallback"/>.</summary>
        static void Skin(Image img, Sprite sprite, Color fallback, bool sliced)
        {
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
                img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
                img.preserveAspect = !sliced;
                if (sliced) RoundEnds(img, img.rectTransform.sizeDelta.y);
            }
            else
            {
                img.sprite = EditorUI.RoundRect;
                img.color = fallback;
                img.type = Image.Type.Sliced;
            }
        }

        static Image Img(string name, Transform parent, Sprite sprite, Color fallback, Vector2 pos, Vector2 size, bool sliced = false)
        {
            var img = Rect(name, parent, pos, size).gameObject.AddComponent<Image>();
            Skin(img, sprite, fallback, sliced);
            return img;
        }

        /// <summary>A sliced stadium sprite (borders of half its height: the gold pill) keeps round ends at any height.</summary>
        static void RoundEnds(Image img, float height)
        {
            var s = img.sprite;
            float ends = s.border.y + s.border.w;
            img.pixelsPerUnitMultiplier = ends >= s.rect.height * 0.9f && height > 0f ? ends / height : 1f;
        }

        static Text Label(string name, Transform parent, Vector2 pos, Vector2 size, string text, int fontSize, Color color)
        {
            var t = Rect(name, parent, pos, size).gameObject.AddComponent<Text>();
            t.font = EditorUI.Font;
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = WordArt.Style(FontStyle.Bold);
            t.alignment = TextAnchor.MiddleCenter;
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
            b.onClick.AddListener(onClick);
            Haptic.Bind(b);
            if (icon != null) Img("icon", bg.transform, icon, Color.white, new Vector2(0f, 4f), new Vector2(size * 0.68f, size * 0.68f)).raycastTarget = false;
            else Label("label", bg.transform, Vector2.zero, Vector2.zero, fallbackLabel, Mathf.RoundToInt(size * 0.45f), Color.white);
            return b;
        }

        /// <summary>Green count circle with a gold number, at <paramref name="pos"/> of its parent.</summary>
        Image Badge(Transform parent, Vector2 pos, float size, out Text text)
        {
            var badge = Img("badge", parent, _skin != null ? _skin.greenCircle : null, Green, pos, new Vector2(size, size));
            badge.raycastTarget = false;
            text = Title("count", badge.transform, new Vector2(0f, 2f), new Vector2(size, size), "0", Mathf.RoundToInt(size * 0.52f));
            return badge;
        }

        /// <summary>Small gold "LEVEL n" pill: the LEVEL word picture and digit pictures of the skin (WordLabel), text as the fallback.</summary>
        void LevelPill(string name, Transform parent, Vector2 pos, Vector2 size, int level)
        {
            var bg = Img(name, parent, _skin != null ? _skin.buttonPill : null, Panel, pos, size, true);
            bg.raycastTarget = false;
            float cap = size.y * 0.36f;   // the lettering's cap height inside the pill
            var row = Rect("label", bg.transform, new Vector2(0f, 2f), new Vector2(size.x - 16f, size.y));
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.childAlignment = TextAnchor.MiddleCenter;
            group.spacing = cap * 0.3f;
            group.childControlWidth = group.childControlHeight = false;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            var label = row.gameObject.AddComponent<WordLabel>();
            label.row = row;
            label.digitHeight = cap;
            Sprite word = _skin != null ? _skin.wordLevel : null;
            var wordSize = WordArt.Size(word);
            var img = Rect(WordArt.PictureName, row, Vector2.zero, wordSize.y > 0f ? wordSize * (cap / wordSize.y) : Vector2.zero).gameObject.AddComponent<Image>();
            img.sprite = word;
            img.preserveAspect = true;
            img.raycastTarget = false;
            label.word = img;
            label.fallback = Label("text", row, Vector2.zero, new Vector2(size.x - 16f, cap), "LEVEL " + level, Mathf.RoundToInt(cap * 1.1f), Color.white);
            label.Set("LEVEL " + level, level);
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("ManaGardenCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 404;   // above the game HUD and the level coins, below the mini game and the level editor
            _scaler = canvasGo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1080, 2340);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            if (FindFirstObjectByType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var root = (RectTransform)canvasGo.transform;

            // ---- full-screen panel over the board
            var panel = Rect("ManaGardenPanel", root, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            panel.rectTransform.anchorMin = Vector2.zero;
            panel.rectTransform.anchorMax = Vector2.one;
            panel.color = Dim;
            _panel = panel.gameObject;
            var p = panel.transform;

            // ---- header: settings / title / close (mockup: 133 px squares at y 230, title box 497 x 215 at y 202)
            SquareButton("btn - settings", p, new Vector2(-413f, 625f), 143f, _skin != null ? _skin.iconSettings : null, "S", OpenSettings);
            SquareButton("btn - close", p, new Vector2(413f, 625f), 143f, _skin != null ? _skin.iconClose : null, "X", Close);
            var titleBg = Img("title_panel", p, _skin != null ? _skin.buttonRect : null, Panel, new Vector2(0f, 655f), new Vector2(536f, 232f), true);
            titleBg.raycastTarget = false;
            WordArt.Picture(Title("title", titleBg.transform, new Vector2(0f, 4f), new Vector2(500f, 120f), "Mana Garden", 72), _skin != null ? _skin.wordManaGarden : null);

            // ---- gold-framed panel (mockup frame 835 x 950 at y 795): the picture has ~50 px of transparent margin
            // around its frame (sparkles), so 925 x 1050 shows a 776 x 885 frame; scaled to the mockup's 900 x 1024
            var frame = Img("frame", p, _skin != null ? _skin.popupPanel : null, Panel, new Vector2(0f, 16f), new Vector2(1073f, 1215f));
            frame.raycastTarget = false;
            var f = frame.transform;

            // ---- plant slots: 3 + 2 grid of 212 squares, a 172 x 65 level pill above each
            int count = rules.SlotCount;
            _slotArt = new Image[count];
            var slotSize = new Vector2(197f, 197f) * Scale;
            var pillSize = new Vector2(160f, 60f) * Scale;
            for (int i = 0; i < count; i++)
            {
                bool top = i < 3;
                int col = top ? i : i - 3;
                float x = top ? (col - 1) * 281f : (col - 0.5f) * 280f;
                float y = top ? 161f : -200f;
                var slot = Img("slot_" + i, f, _skin != null ? _skin.buttonSquare : null, Card, new Vector2(x, y), slotSize, true);
                slot.raycastTarget = false;
                var art = Img("plant", slot.transform, null, Green, new Vector2(0f, 4f), new Vector2(150f, 150f));
                art.raycastTarget = false;
                art.gameObject.SetActive(false);
                _slotArt[i] = art;
                LevelPill("level_" + i, f, new Vector2(x, y + 146f), pillSize, rules.LevelOf(i));
            }

            // ---- plant button under the frame (mockup: 224 circle at y 1329, badge 100 at its top right)
            var plantBg = Img("btn - plant", p, _skin != null ? _skin.buttonCircle : null, Card, new Vector2(0f, -560f), new Vector2(241f, 241f));
            var plant = plantBg.gameObject.AddComponent<Button>();
            plant.targetGraphic = plantBg;
            plant.onClick.AddListener(Plant);
            Haptic.Bind(plant);
            Img("icon", plantBg.transform, _skin != null ? _skin.iconPlant : null, Green, new Vector2(0f, 4f), new Vector2(170f, 170f)).raycastTarget = false;
            _badge = Badge(plantBg.transform, new Vector2(87f, 95f), 108f, out _badgeText);
        }

        // ---- debug helpers (component context menu)
        [ContextMenu("Unlock next plant")]
        void DebugUnlockNext()
        {
            for (int i = 0; i < rules.SlotCount; i++)
                if (!rules.IsUnlocked(i)) { rules.OnLevelWon(rules.LevelOf(i)); break; }
            if (IsOpen) Refresh(); else RefreshBadges();
        }

        [ContextMenu("Reset garden")]
        void DebugReset()
        {
            rules.Reset();
            if (IsOpen) Refresh(); else RefreshBadges();
        }
    }
}
