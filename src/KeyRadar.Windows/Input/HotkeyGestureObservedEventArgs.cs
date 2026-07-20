using KeyRadar.Shortcuts;

namespace KeyRadar.Windows.Input;

public sealed class ShortcutGestureObservedEventArgs(ShortcutGesture gesture) : EventArgs
{
    public ShortcutGesture Gesture { get; } = gesture;
}
