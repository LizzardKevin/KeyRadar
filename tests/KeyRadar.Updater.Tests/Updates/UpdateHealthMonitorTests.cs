using KeyRadar.Updater.Updates;

namespace KeyRadar.Updater.Tests.Updates;

public sealed class UpdateHealthMonitorTests
{
    [Fact]
    public async Task WaitForMarkerAsync_ReturnsTrueWhenApplicationSignalsHealth()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"KeyRadar-Health-{Guid.NewGuid():N}.ok");
        try
        {
            File.WriteAllText(marker, "ok");

            var result = await UpdateHealthMonitor.WaitForMarkerAsync(
                marker,
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken);

            Assert.True(result);
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task WaitForMarkerAsync_ReturnsFalseAfterTimeout()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"KeyRadar-Health-{Guid.NewGuid():N}.ok");

        var result = await UpdateHealthMonitor.WaitForMarkerAsync(
            marker,
            TimeSpan.FromMilliseconds(30),
            TestContext.Current.CancellationToken);

        Assert.False(result);
    }
}
