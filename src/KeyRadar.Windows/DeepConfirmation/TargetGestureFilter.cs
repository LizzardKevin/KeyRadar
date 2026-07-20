using KeyRadar.Shortcuts;

namespace KeyRadar.Windows.DeepConfirmation;

public sealed class TargetGestureFilter(ShortcutGesture target)
{
    public bool Observe(ShortcutGesture gesture) => gesture == target;
}
