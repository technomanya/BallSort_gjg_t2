// iOS side of BallSortGame.Haptics.IosHaptics: maps the Android HapticFeedbackConstants used by the
// haptic patterns onto UIKit feedback generators.
#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *gLight, *gMedium, *gHeavy;
static UISelectionFeedbackGenerator *gSelection;
static UINotificationFeedbackGenerator *gNotification;

extern "C" void BallSortHaptics_Play(int androidConstant)
{
    if (@available(iOS 10.0, *)) {
        switch (androidConstant) {
            case 16: // CONFIRM
                if (!gNotification) gNotification = [[UINotificationFeedbackGenerator alloc] init];
                [gNotification notificationOccurred:UINotificationFeedbackTypeSuccess];
                break;
            case 17: // REJECT
                if (!gNotification) gNotification = [[UINotificationFeedbackGenerator alloc] init];
                [gNotification notificationOccurred:UINotificationFeedbackTypeError];
                break;
            case 0:  // LONG_PRESS
                if (!gHeavy) gHeavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
                [gHeavy impactOccurred];
                break;
            case 1:  // VIRTUAL_KEY
            case 3:  // KEYBOARD_PRESS
                if (!gMedium) gMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
                [gMedium impactOccurred];
                break;
            case 4:  // CLOCK_TICK
            case 6:  // CONTEXT_CLICK
            case 26: // SEGMENT_TICK
            case 27: // SEGMENT_FREQUENT_TICK
                if (!gSelection) gSelection = [[UISelectionFeedbackGenerator alloc] init];
                [gSelection selectionChanged];
                break;
            default:
                if (!gLight) gLight = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
                [gLight impactOccurred];
                break;
        }
    }
}
