using System.Collections.Generic;
using BallSortGame.UI;
using UnityEngine;

namespace BallSortGame.Haptics
{
    /// <summary>
    /// Entry point: Haptic.Play(type). Queues the event's pattern and a LateUpdate runner fires its steps on time.
    /// Patterns are sorted by priority so the important one fires first when several start in one frame.
    /// </summary>
    public static class Haptic
    {
        static HapticsConfig _config;
        static Runner _runner;
        static AndroidHaptics _android;
        static bool? _supported;

        /// <summary>Editor only: log each step instead of vibrating.</summary>
        public static bool LogInEditor = false;

        public static HapticsConfig Config
        {
            get
            {
                if (_config == null) _config = Resources.Load<HapticsConfig>("Haptics");
                if (_config == null) _config = HapticsConfig.CreateDefault();
                return _config;
            }
            set { _config = value; }
        }

        /// <summary>Android 8+ with a vibrator, or iOS.</summary>
        public static bool IsSupported
        {
            get
            {
                if (_supported.HasValue) return _supported.Value;
#if UNITY_EDITOR
                return LogInEditor;   // not cached: the flag may be flipped while playing
#elif UNITY_ANDROID
                if (_android == null) _android = new AndroidHaptics();
                _supported = _android.IsSupported;
#elif UNITY_IOS
                _supported = IosHaptics.IsSupported;
#else
                _supported = false;
#endif
                return _supported.Value;
            }
        }

        /// <summary>The settings page toggle.</summary>
        public static bool IsEnabled => SettingsView.Haptic;

        public static void Play(HapticType type)
        {
            if (type == HapticType.None || !IsSupported || !IsEnabled) return;
            var pattern = Config.Get(type);
            if (pattern == null || pattern.delays == null || pattern.delays.Length == 0) return;
            if (pattern.feedbacks == null || pattern.feedbacks.Length != pattern.delays.Length)
            {
                Debug.LogWarning($"Haptics: pattern {type} has {pattern.delays?.Length ?? 0} delays but {pattern.feedbacks?.Length ?? 0} feedbacks; skipped.");
                return;
            }
            EnsureRunner().Add(pattern);
        }

        /// <summary>Adds a ButtonClick haptic in front of a button's own handlers.</summary>
        public static void Bind(UnityEngine.UI.Button button)
        {
            if (button != null) button.onClick.AddListener(() => Play(HapticType.ButtonClick));
        }

        static Runner EnsureRunner()
        {
            if (_runner != null) return _runner;
            var go = new GameObject("Haptics");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _runner = go.AddComponent<Runner>();
            return _runner;
        }

        static void Perform(AndroidHapticFeedback feedback)
        {
#if UNITY_EDITOR
            if (LogInEditor) Debug.Log("Haptics: " + feedback);
#elif UNITY_ANDROID
            _android?.Perform(feedback);
#elif UNITY_IOS
            IosHaptics.Perform(feedback);
#endif
        }

        // A pattern in flight: elapsed ms, total ms, which steps already fired.
        sealed class Feedback
        {
            public HapticsConfig.Pattern Pattern;
            public float Elapsed;
            public float Total;
            public readonly List<bool> Played = new List<bool>();

            public void Init(HapticsConfig.Pattern p)
            {
                Pattern = p;
                Elapsed = 0f;
                Total = 0f;
                Played.Clear();
                for (int i = 0; i < p.delays.Length; i++) { Played.Add(false); Total += p.delays[i]; }
            }
        }

        // Advances patterns by frame time and fires the steps that came due.
        sealed class Runner : MonoBehaviour
        {
            readonly List<Feedback> _running = new List<Feedback>();
            readonly List<Feedback> _addedThisFrame = new List<Feedback>();
            readonly Stack<Feedback> _pool = new Stack<Feedback>();

            public void Add(HapticsConfig.Pattern pattern)
            {
                var f = _pool.Count > 0 ? _pool.Pop() : new Feedback();
                f.Init(pattern);
                _running.Add(f);
                _addedThisFrame.Add(f);
                if (_running.Count >= 2) _running.Sort((a, b) => b.Pattern.priority.CompareTo(a.Pattern.priority));
            }

            void LateUpdate()
            {
                if (_running.Count == 0) return;
                float dtMs = Time.deltaTime * 1000f;
                foreach (var f in _running)
                    if (!_addedThisFrame.Contains(f)) f.Elapsed += dtMs;

                foreach (var f in _running)
                {
                    int due = 0;
                    var d = f.Pattern.delays;
                    for (int i = 0; i < d.Length; i++)
                    {
                        due += d[i];
                        if (due > f.Elapsed) break;
                        if (f.Played[i]) continue;
                        f.Played[i] = true;
                        Perform(f.Pattern.feedbacks[i]);
                    }
                }

                for (int i = _running.Count - 1; i >= 0; i--)
                {
                    var f = _running[i];
                    if (f.Total > f.Elapsed) continue;
                    _running.RemoveAt(i);
                    _pool.Push(f);
                }
                _addedThisFrame.Clear();
            }

            void OnDestroy()
            {
                _android?.Dispose();
                _android = null;
                _supported = null;
                if (_runner == this) _runner = null;
            }
        }
    }
}
