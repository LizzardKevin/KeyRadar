using System.Diagnostics;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Foreground;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Applications;

public sealed class WindowsInteropSmokeTests
{
    private sealed class AlwaysSafeProbeGate : IHotkeyProbeSafetyGate
    {
        public HotkeyProbeSafety Check() => HotkeyProbeSafety.Safe;
    }

    [Fact]
    public void ProcessAndWindowSources_ResolveTheirNativeEntryPoints()
    {
        var processes = Record.Exception(() => new SystemProcessSource().ReadProcesses());
        var windows = Record.Exception(() => new Win32WindowSource().ReadWindows());
        var foreground = Record.Exception(ForegroundApplicationReader.Read);

        Assert.Null(processes);
        Assert.Null(windows);
        Assert.Null(foreground);
    }

    [Fact]
    public void HotkeyProbe_ResolvesItsNativeEntryPoints()
    {
        var api = new Win32HotkeyRegistrationApi();
        var gesture = HotkeyGesture.Parse("Ctrl+Alt+F24");

        var exception = Record.Exception(() => api.TryRegister(0x4B52, gesture));
        api.Unregister(0x4B52);

        Assert.Null(exception);
    }

    [Fact]
    public async Task StandardHotkeyProbe_CompletesAndReleasesEveryTemporaryRegistration()
    {
        var candidates = StandardGlobalHotkeyCandidateSource.Create();
        using var api = new Win32HotkeyRegistrationApi();
        var scanner = new GlobalHotkeyOccupancyScanner(
            new GlobalHotkeyAvailabilityProbe(api),
            new AlwaysSafeProbeGate());

        var started = Stopwatch.StartNew();
        var results = await scanner.ScanAsync(candidates, progress: null, TestContext.Current.CancellationToken);

        Assert.Equal(candidates.Count, results.Count);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"Probe took {started.Elapsed}.");
    }

}
