using KeyRadar.Windows.Evidence;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class ScanGenerationCoordinatorTests
{
    [Fact]
    public void Old_scan_callbacks_are_ignored_after_a_new_scan_becomes_current()
    {
        var coordinator = new ScanGenerationCoordinator();
        var oldScan = coordinator.Begin();
        var applied = new List<string>();
        var newScan = coordinator.Begin();

        Assert.False(coordinator.TryApply(oldScan, () => applied.Add("old progress")));
        Assert.False(coordinator.TryApply(oldScan, () => applied.Add("old canceled")));
        Assert.False(coordinator.TryApply(oldScan, () => applied.Add("old completed")));
        Assert.True(coordinator.TryApply(newScan, () => applied.Add("new progress")));
        Assert.True(coordinator.TryApply(newScan, () => applied.Add("new completed")));

        Assert.Equal(["new progress", "new completed"], applied);
    }
}
