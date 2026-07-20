using KeyRadar.Hotkeys;
using KeyRadar.Windows.DeepConfirmation;

namespace KeyRadar.Windows.Tests.DeepConfirmation;

public sealed class TargetGestureFilterTests
{
    [Fact]
    public void Only_the_requested_combination_can_complete_confirmation()
    {
        var filter = new TargetGestureFilter(HotkeyGesture.Parse("Alt+A"));

        Assert.False(filter.Observe(HotkeyGesture.Parse("Ctrl+A")));
        Assert.False(filter.Observe(HotkeyGesture.Parse("Alt+S")));
        Assert.True(filter.Observe(HotkeyGesture.Parse("Alt+A")));
    }
}
