using System.Diagnostics;
using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Tests.Applications;

public sealed class RunningApplicationScannerPerformanceTests
{
    [Fact]
    [Trait("Category", "Performance")]
    public void Real_machine_scan_completes_within_five_seconds()
    {
        var scanner = new RunningApplicationScanner(
            new SystemProcessSource(),
            new Win32WindowSource());
        var stopwatch = Stopwatch.StartNew();

        var snapshots = scanner.Scan(Environment.ProcessId);

        stopwatch.Stop();
        Assert.NotEmpty(snapshots);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Scanning {snapshots.Count} process snapshots took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    }
}
