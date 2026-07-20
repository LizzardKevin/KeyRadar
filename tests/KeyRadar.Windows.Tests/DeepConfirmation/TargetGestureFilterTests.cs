using KeyRadar.Shortcuts;
using KeyRadar.Windows.DeepConfirmation;

namespace KeyRadar.Windows.Tests.DeepConfirmation;

public sealed class TargetGestureFilterTests
{
    [Fact]
    public void Only_the_requested_combination_can_complete_confirmation()
    {
        var filter = new TargetGestureFilter(ShortcutGesture.Parse("Alt+A"));

        Assert.False(filter.Observe(ShortcutGesture.Parse("Ctrl+A")));
        Assert.False(filter.Observe(ShortcutGesture.Parse("Alt+S")));
        Assert.True(filter.Observe(ShortcutGesture.Parse("Alt+A")));
    }
}
