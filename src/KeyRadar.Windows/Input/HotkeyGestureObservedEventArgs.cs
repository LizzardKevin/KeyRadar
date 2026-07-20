using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.Input;

public sealed class HotkeyGestureObservedEventArgs(HotkeyGesture gesture) : EventArgs
{
    public HotkeyGesture Gesture { get; } = gesture;
}
