using UnityEngine;

namespace BallSortGame.Haptics
{
    /// <summary>
    /// Android bridge: View.performHapticFeedback on the Unity player's focused view, like Magic Sort's
    /// com.grandgames.watersort.AndroidVibration (including its CONFIRM fallback for old API levels), done through
    /// AndroidJavaObject so no Java plugin is needed. Supported on API 26+ with a vibrator, as the reference requires.
    /// </summary>
    public sealed class AndroidHaptics
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject _view;
        AndroidJavaObject _vibrator;
        int _sdk;
        bool _supported;

        public AndroidHaptics()
        {
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    _sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    _view = activity.Call<AndroidJavaObject>("getCurrentFocus");
                    if (_view == null)
                        using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                            _view = window.Call<AndroidJavaObject>("getDecorView");
                }
                _supported = _sdk >= 26 && _view != null && _vibrator != null && _vibrator.Call<bool>("hasVibrator");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("AndroidHaptics: unavailable (" + e.Message + ")");
                _supported = false;
            }
        }

        public bool IsSupported => _supported;

        public void Perform(AndroidHapticFeedback feedback)
        {
            if (!_supported) return;
            int type = (int)feedback;
            if (type == (int)AndroidHapticFeedback.CONFIRM)
                type = _sdk >= 30 ? (int)AndroidHapticFeedback.CONFIRM
                     : _sdk >= 27 ? (int)AndroidHapticFeedback.KEYBOARD_PRESS
                     : (int)AndroidHapticFeedback.LONG_PRESS;
            try { _view.Call<bool>("performHapticFeedback", type); }
            catch (System.Exception e) { Debug.LogWarning("AndroidHaptics: " + e.Message); _supported = false; }
        }

        public void Dispose()
        {
            _view?.Dispose(); _view = null;
            _vibrator?.Dispose(); _vibrator = null;
        }
#else
        public bool IsSupported => false;
        public void Perform(AndroidHapticFeedback feedback) { }
        public void Dispose() { }
#endif
    }
}
