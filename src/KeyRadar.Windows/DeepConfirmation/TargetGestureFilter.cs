using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.DeepConfirmation;

public sealed class TargetGestureFilter(HotkeyGesture target)
{
    public bool Observe(HotkeyGesture gesture) => gesture == target;
}
