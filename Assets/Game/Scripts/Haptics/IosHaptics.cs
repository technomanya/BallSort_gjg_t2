using System.Runtime.InteropServices;

namespace BallSortGame.Haptics
{
    /// <summary>
    /// iOS bridge to Plugins/iOS/BallSortHaptics.mm (UIKit feedback generators). Magic Sort's iOS pattern assets carry
    /// only delays and priority (the Android APK has no iOS code), so each step plays one generator picked from the
    /// Android constant of that step.
    /// </summary>
    public static class IosHaptics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void BallSortHaptics_Play(int androidConstant);
        public static bool IsSupported => true;
        public static void Perform(AndroidHapticFeedback feedback) => BallSortHaptics_Play((int)feedback);
#else
        public static bool IsSupported => false;
        public static void Perform(AndroidHapticFeedback feedback) { }
#endif
    }
}
