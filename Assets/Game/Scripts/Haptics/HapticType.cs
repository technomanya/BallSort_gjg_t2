namespace BallSortGame.Haptics
{
    /// <summary>Game events that may vibrate.</summary>
    public enum HapticType
    {
        None = 0,
        ButtonClick = 1,
        TubeSelect = 2,
        PourStart = 3,
        PourEnd = 4,
        TubeDeselect = 5,
        LevelWin = 6,
        LevelLose = 7,
        LevelStart = 8,
        Reward = 9,
    }

    /// <summary>
    /// android.view.HapticFeedbackConstants. Android ignores constants newer than the device's API level.
    /// </summary>
    public enum AndroidHapticFeedback
    {
        LONG_PRESS = 0,
        VIRTUAL_KEY = 1,
        KEYBOARD_PRESS = 3,
        CLOCK_TICK = 4,
        CONTEXT_CLICK = 6,
        KEYBOARD_RELEASE = 7,
        VIRTUAL_KEY_RELEASE = 8,
        TEXT_HANDLE_MOVE = 9,
        GESTURE_START = 12,
        GESTURE_END = 13,
        CONFIRM = 16,
        REJECT = 17,
        TOGGLE_ON = 21,
        TOGGLE_OFF = 22,
        GESTURE_THRESHOLD_ACTIVATE = 23,
        GESTURE_THRESHOLD_DEACTIVATE = 24,
        DRAG_START = 25,
        SEGMENT_TICK = 26,
        SEGMENT_FREQUENT_TICK = 27,
    }
}
