using System.Collections;
using BallSortGame.Core;
using BallSortGame.Gameplay;
using BallSortGame.Haptics;
using BallSortGame.LevelEditor;
using BallSortGame.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.Garden
{
    /// <summary>
    /// Mana Garden page: five plant slots and the plant button with the seed count. The page is laid out in the scene.
    /// It also drives the home page pots and shows the seed on the win screen when a plant unlocks.
    /// Rules live in <see cref="ManaGarden"/>.
    /// </summary>
    public class ManaGardenView : MonoBehaviour
    {
        static readonly Color Gold = new Color(1f, 0.72f, 0.12f, 1f);
        static readonly Color Navy = new Color(0.12f, 0.06f, 0.30f, 1f);
        static readonly Color Card = new Color(0.52f, 0.12f, 0.42f, 1f);
        static readonly Color Green = new Color(0.25f, 0.85f, 0.45f, 1f);
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        public GameManager game;
        public ManaGarden rules = new ManaGarden();

        [Header("Layout")]
        [Tooltip("Menu plant button offset from the menu's top right.")]
        public Vector2 MenuButtonPosition = new Vector2(-150f, -133f);
        public float MenuButtonSize = 140f;

        [Header("Scene widgets (wired by hand in the scene)")]
        [Tooltip("The full-screen page, inactive while closed.")]
        public GameObject panel;
        public Button settingsButton;
        public Button closeButton;
        [Tooltip("The big plant button under the frame.")]
        public Button plantButton;
        [Tooltip("Seed / plant picture of each slot, in slot order.")]
        public Image[] slotArt = new Image[0];
        [Tooltip("Seed count badge on the plant button and its number.")]
        public Image badge;
        public Text badgeText;
        [Tooltip("Home page: show \"+\" on locked empty pots too.")]
        public bool AddOnLockedPots = true;
        [Tooltip("Home page: young flower scale against the lit one.")]
        [Range(0.1f, 1f)] public float YoungFlowerScale = 0.5f;

        UISkin _skin;
        GameHUD _hud;
        Image _menuBadge, _winCoin;
        Text _menuBadgeText;
        Sprite _winCoinSprite;
        Button _menuButton;
        MainMenuView.Pot[] _menuPots;

        void Start()
        {
            rules.Load();
            _skin = UISkin.Current;
            _hud = FindFirstObjectByType<GameHUD>();
            Wire();
            panel.SetActive(false);
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
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
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
            panel.SetActive(false);
            if (game != null) game.IsPaused = MainMenuView.IsOpen;   // opened from the menu: stay paused under it
        }

        // closes first because the page draws above the HUD's settings
        void OpenSettings()
        {
            Close();
            if (_hud != null) _hud.OpenSettings();
        }

        void Plant() => Planted(rules.Plant());

        // "+" on a home page pot plants that slot, a locked pot opens the garden page instead
        void PlantSlot(int slot)
        {
            if (rules.Plant(slot)) { Planted(slot); return; }
            if (!rules.IsUnlocked(slot)) Open();
        }

        void Planted(int slot)
        {
            Refresh();
            if (slot < 0) return;
            Haptic.Play(HapticType.Reward);
            if (slotArt != null && slot < slotArt.Length && slotArt[slot] != null && IsOpen) StartCoroutine(Punch(slotArt[slot].rectTransform));
            PopPotFlower(slot);
        }

        // an unlock shows the seed in place of the win screen coin, a newly lit flower pops in its pot
        void OnLevelWon(int level)
        {
            if (game == null || game.IsBotPlaying || Testing()) return;
            var wasLit = new bool[rules.SlotCount];
            for (int i = 0; i < wasLit.Length; i++) wasLit[i] = rules.IsLit(i);
            bool unlocked = rules.OnLevelWon(level), grown = false;
            for (int i = 0; i < wasLit.Length; i++) grown |= !wasLit[i] && rules.IsLit(i);
            if (!unlocked && !grown) return;
            RefreshBadges();
            for (int i = 0; i < wasLit.Length; i++) if (!wasLit[i] && rules.IsLit(i)) PopPotFlower(i);
            if (unlocked) ShowSeedOnWinScreen();
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

        // pops the rect once and leaves it at its resting scale
        IEnumerator Punch(RectTransform rt, float baseScale = 1f)
        {
            for (float k = 0f; k < 1f;)
            {
                k = Mathf.Min(1f, k + Time.unscaledDeltaTime / 0.45f);
                rt.localScale = Vector3.one * (baseScale * (1f + 0.35f * Mathf.Sin(k * Mathf.PI)));
                yield return null;
            }
            rt.localScale = Vector3.one * baseScale;
        }

        float FlowerScale(int slot) => rules.IsLit(slot) ? 1f : YoungFlowerScale;

        void PopPotFlower(int slot)
        {
            if (_menuPots != null && slot < _menuPots.Length && _menuPots[slot] != null && _menuPots[slot].flower != null && _menuPots[slot].flower.gameObject.activeInHierarchy)
                StartCoroutine(Punch(_menuPots[slot].flower.rectTransform, FlowerScale(slot)));
        }

        void Refresh()
        {
            if (slotArt != null)
                for (int i = 0; i < slotArt.Length; i++)
                {
                    var art = slotArt[i];
                    if (art == null) continue;
                    bool planted = rules.IsPlanted(i), unlocked = rules.IsUnlocked(i);
                    art.gameObject.SetActive(unlocked);
                    if (!unlocked) continue;
                    // the seed waits in the slot until planted, then the plant shows
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
            SetBadge(badge, badgeText, seeds);
            SetBadge(_menuBadge, _menuBadgeText, seeds);
            RefreshMenuPots();
        }

        // a planted pot shows its flower, an empty one its "+"
        void RefreshMenuPots()
        {
            if (_menuPots == null) return;
            for (int i = 0; i < _menuPots.Length; i++)
            {
                var pot = _menuPots[i];
                if (pot == null) continue;
                bool unlocked = rules.IsUnlocked(i), planted = rules.IsPlanted(i);
                if (pot.flower != null)
                {
                    // small young flower until it grows, then the lit one at full size (same box and pivot)
                    var sprite = planted ? FlowerSprite(i, rules.IsLit(i)) : null;
                    if (sprite != null) pot.flower.sprite = sprite;
                    pot.flower.rectTransform.localScale = Vector3.one * FlowerScale(i);
                    pot.flower.gameObject.SetActive(unlocked && planted && pot.flower.sprite != null);
                }
                if (pot.addButton != null) pot.addButton.gameObject.SetActive(!planted && (unlocked || AddOnLockedPots));
            }
        }

        // the lit picture when grown, falling back to the plain one
        Sprite FlowerSprite(int slot, bool lit)
        {
            if (_skin == null) return null;
            if (lit && _skin.flowersLit != null && slot < _skin.flowersLit.Length && _skin.flowersLit[slot] != null) return _skin.flowersLit[slot];
            return _skin.flowers != null && slot < _skin.flowers.Length ? _skin.flowers[slot] : null;
        }

        static void SetBadge(Image badge, Text text, int count)
        {
            if (badge == null) return;
            badge.gameObject.SetActive(count > 0);
            if (text != null) text.text = count.ToString();
        }

        // --- main menu button

        // adds a seed badge to the menu's garden button, or makes a button top right when the menu has none
        void AttachMenuButton()
        {
            var menu = FindFirstObjectByType<MainMenuView>(FindObjectsInactive.Include);
            if (menu == null) return;
            bool made = menu.gardenButton == null;
            if (made)
            {
                var rt = Rect("btn - mana_garden", menu.transform, MenuButtonPosition, new Vector2(MenuButtonSize, MenuButtonSize));
                rt.anchorMin = rt.anchorMax = Vector2.one;
                var bg = rt.gameObject.AddComponent<Image>();
                Skin(bg, _skin != null ? _skin.buttonCircle : null, Card);
                var b = bg.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                Img("icon", bg.transform, _skin != null ? _skin.iconPlant : null, Green, new Vector2(0f, 4f), new Vector2(MenuButtonSize * 0.62f, MenuButtonSize * 0.62f)).raycastTarget = false;
                _menuBadge = Badge(bg.transform, new Vector2(MenuButtonSize * 0.36f, MenuButtonSize * 0.4f), 64f, out _menuBadgeText);
                menu.gardenButton = b;
            }
            else if (menu.gardenButton.transform.Find("badge") == null)
            {
                float size = ((RectTransform)menu.gardenButton.transform).sizeDelta.x;
                _menuBadge = Badge(menu.gardenButton.transform, new Vector2(size * 0.36f, size * 0.4f), 64f, out _menuBadgeText);
            }
            _menuButton = menu.gardenButton;
            if (made)   // the menu's own button already opens the garden with a haptic
            {
                _menuButton.onClick.RemoveListener(Open);
                _menuButton.onClick.AddListener(Open);
                Haptic.Bind(_menuButton);
            }
            _menuPots = menu.pots;
            if (_menuPots != null)
                for (int i = 0; i < _menuPots.Length; i++)
                {
                    int slot = i;
                    var b = _menuPots[i] != null ? _menuPots[i].addButton : null;
                    if (b == null) continue;
                    b.onClick.AddListener(() => PlantSlot(slot));
                    Haptic.Bind(b);
                }
            RefreshBadges();
        }

        // --- UI helpers for the menu button and badge

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

        // skin sprite, else a plain rounded shape in the fallback colour
        static void Skin(Image img, Sprite sprite, Color fallback)
        {
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
            }
            else
            {
                img.sprite = EditorUI.RoundRect;
                img.color = fallback;
                img.type = Image.Type.Sliced;
            }
        }

        static Image Img(string name, Transform parent, Sprite sprite, Color fallback, Vector2 pos, Vector2 size)
        {
            var img = Rect(name, parent, pos, size).gameObject.AddComponent<Image>();
            Skin(img, sprite, fallback);
            return img;
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

        // green count circle with a gold number
        Image Badge(Transform parent, Vector2 pos, float size, out Text text)
        {
            var badge = Img("badge", parent, _skin != null ? _skin.greenCircle : null, Green, pos, new Vector2(size, size));
            badge.raycastTarget = false;
            text = Title("count", badge.transform, new Vector2(0f, 2f), new Vector2(size, size), "0", Mathf.RoundToInt(size * 0.52f));
            return badge;
        }

        // button listeners are not saved with the scene, so they are added here
        void Wire()
        {
            if (settingsButton != null) { settingsButton.onClick.AddListener(OpenSettings); Haptic.Bind(settingsButton); }
            if (closeButton != null) { closeButton.onClick.AddListener(Close); Haptic.Bind(closeButton); }
            if (plantButton != null) { plantButton.onClick.AddListener(Plant); Haptic.Bind(plantButton); }
        }

        // ---- debug helpers (component context menu)
        [ContextMenu("Unlock next plant")]
        void DebugUnlockNext()
        {
            for (int i = 0; i < rules.SlotCount; i++)
                if (!rules.IsUnlocked(i)) { rules.OnLevelWon(rules.LevelOf(i)); break; }
            if (IsOpen) Refresh(); else RefreshBadges();
        }

        [ContextMenu("Grow next flower")]
        void DebugGrowNext()
        {
            for (int i = 0; i < rules.SlotCount; i++)
                if (rules.IsPlanted(i) && !rules.IsLit(i)) { rules.OnLevelWon(rules.GrowLevelOf(i)); break; }
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
