using System;
using UnityEngine;

namespace BallSortGame.Haptics
{
    /// <summary>
    /// One pattern per <see cref="HapticType"/>: haptic steps, each played <c>delays[i]</c> ms after the previous one
    /// (the first delay counts from the start), and a priority that orders patterns started in the same frame.
    /// Same shape as Magic Sort's Vibration / AndroidFeedbackConfig assets (Delays, Feedbacks, Priority).
    /// Loaded from Resources/Haptics when that asset exists, otherwise <see cref="CreateDefault"/> supplies the
    /// values Magic Sort 0.0.3420 ships.
    /// </summary>
    [CreateAssetMenu(fileName = "Haptics", menuName = "BallSort/Haptics Config")]
    public class HapticsConfig : ScriptableObject
    {
        [Serializable]
        public class Pattern
        {
            public HapticType type;
            [Tooltip("Milliseconds between steps; delays[0] counts from the start. Empty = no vibration for this event.")]
            public int[] delays = new int[0];
            [Tooltip("Android HapticFeedbackConstants per step, one per delay.")]
            public AndroidHapticFeedback[] feedbacks = new AndroidHapticFeedback[0];
            [Tooltip("Patterns started in the same frame play in descending priority.")]
            public int priority;
        }

        public Pattern[] patterns = new Pattern[0];

        public Pattern Get(HapticType type)
        {
            if (patterns == null) return null;
            foreach (var p in patterns) if (p != null && p.type == type) return p;
            return null;
        }

        static Pattern P(HapticType t, int priority, int[] delays, params AndroidHapticFeedback[] feedbacks)
            => new Pattern { type = t, priority = priority, delays = delays, feedbacks = feedbacks };

        static AndroidHapticFeedback[] Repeat(AndroidHapticFeedback f, int n)
        {
            var a = new AndroidHapticFeedback[n];
            for (int i = 0; i < n; i++) a[i] = f;
            return a;
        }

        static int[] Every(int first, int step, int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i == 0 ? first : step;
            return a;
        }

        /// <summary>Magic Sort's shipped VibrationConfigs (buttonclick, bottleselect, bottlefillstart, ... assets).</summary>
        public static HapticsConfig CreateDefault()
        {
            var c = CreateInstance<HapticsConfig>();
            c.name = "Haptics (default)";
            const AndroidHapticFeedback Tick = AndroidHapticFeedback.CONTEXT_CLICK;
            c.patterns = new[]
            {
                P(HapticType.ButtonClick,  0, new[] { 0 }, AndroidHapticFeedback.CONFIRM),
                P(HapticType.TubeSelect,   0, new[] { 0 }, AndroidHapticFeedback.LONG_PRESS),
                P(HapticType.PourStart,    0, Every(0, 100, 8), Repeat(Tick, 8)),
                P(HapticType.PourEnd,      3, new[] { 0 }, AndroidHapticFeedback.VIRTUAL_KEY),
                P(HapticType.TubeDeselect, 0, new int[0]),
                P(HapticType.LevelWin,     3, new[] { 0, 150, 50, 50, 50, 50, 50, 600 },
                    Tick, Tick, Tick, Tick, Tick, Tick, Tick, AndroidHapticFeedback.VIRTUAL_KEY),
                P(HapticType.LevelLose,    0, new[] { 0 }, AndroidHapticFeedback.REJECT),
                P(HapticType.LevelStart,   0, new int[0]),
                P(HapticType.Reward,       0, new[] { 0 }, AndroidHapticFeedback.CONFIRM),
            };
            return c;
        }
    }
}
