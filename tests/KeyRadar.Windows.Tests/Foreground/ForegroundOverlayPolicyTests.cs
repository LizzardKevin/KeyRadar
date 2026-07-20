using KeyRadar.Windows.Foreground;

namespace KeyRadar.Windows.Tests.Foreground;

public sealed class ForegroundOverlayPolicyTests
{
    [Theory]
    [InlineData(100, 0, true, false)]
    [InlineData(100, 100, true, false)]
    [InlineData(100, 200, false, false)]
    [InlineData(100, 200, true, true)]
    public void Overlay_visibility_follows_foreground_and_rule_availability(
        int currentProcessId,
        int foregroundProcessId,
        bool hasAvailableRules,
        bool expected)
    {
        Assert.Equal(
            expected,
            ForegroundOverlayPolicy.ShouldShow(currentProcessId, foregroundProcessId, hasAvailableRules));
    }
}
