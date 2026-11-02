using System;
using BallSortGame.Haptics;
using UnityEngine;
using UnityEngine.UI;

namespace BallSortGame.UI
{
    /// <summary>
    /// Settings page with haptic, music and sound rows. Each row's button mutes and restores its slider.
    /// Values live in PlayerPrefs.
    /// </summary>
    public class SettingsView : MonoBehaviour
    {
        public const string HapticKey = "ballsort.haptic";
        public const string MusicKey = "ballsort.music";
        public const string SoundKey = "ballsort.sound";

        public static bool Haptic => PlayerPrefs.GetFloat(HapticKey, 1f) > 0f;
        public static float SoundVolume => PlayerPrefs.GetFloat(SoundKey, 1f);

        [Serializable]
        public class Row
        {
            public Button button;
            public Slider slider;
            public Image icon;
            [NonSerialized] public float LastOn = 1f;
        }

        public Row haptic;
        public Row music;
        public Row sound;
        [Tooltip("Icon tint while the row is muted.")]
        public Color iconOff = new Color(1f, 1f, 1f, 0.45f);

        // runs at start-up so the first sound already uses the saved level
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplySaved() => AudioListener.volume = SoundVolume;

        void Awake()
        {
            Bind(haptic, HapticKey);
            Bind(music, MusicKey);
            Bind(sound, SoundKey);
        }

        void OnEnable()
        {
            Load(haptic, HapticKey);
            Load(music, MusicKey);
            Load(sound, SoundKey);
        }

        void Bind(Row row, string key)
        {
            if (row == null) return;
            if (row.slider != null) row.slider.onValueChanged.AddListener(v => Set(row, key, v));
            if (row.button == null) return;
            BallSortGame.Haptics.Haptic.Bind(row.button);
            row.button.onClick.AddListener(() => Toggle(row, key));
        }

        void Load(Row row, string key)
        {
            if (row == null) return;
            float v = PlayerPrefs.GetFloat(key, 1f);
            if (v > 0f) row.LastOn = v;
            if (row.slider != null) row.slider.SetValueWithoutNotify(v);
            Paint(row, v);
        }

        void Toggle(Row row, string key)
        {
            float cur = row.slider != null ? row.slider.value : PlayerPrefs.GetFloat(key, 1f);
            float next = cur > 0f ? 0f : row.LastOn;
            if (row.slider != null) row.slider.value = next;   // onValueChanged saves it
            else Set(row, key, next);
        }

        bool _dirty;

        void SaveIfDirty()
        {
            if (!_dirty) return;
            _dirty = false;
            PlayerPrefs.Save();
        }

        void OnDisable() => SaveIfDirty();
        void OnApplicationPause(bool paused) { if (paused) SaveIfDirty(); }

        void Set(Row row, string key, float v)
        {
            if (v > 0f) row.LastOn = v;
            PlayerPrefs.SetFloat(key, v);
            _dirty = true;   // saved once on close, a slider drag would write the prefs file every tick
            Paint(row, v);
            if (key == SoundKey) AudioListener.volume = v;
            if (key == HapticKey && v > 0f) BallSortGame.Haptics.Haptic.Play(HapticType.ButtonClick);   // turning haptics on is felt
        }

        void Paint(Row row, float v)
        {
            if (row.icon != null) row.icon.color = v > 0f ? Color.white : iconOff;
        }
    }
}
