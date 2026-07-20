using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Tests.Applications;

public sealed class RunningApplicationScannerTests
{
    [Fact]
    public void Scan_excludes_KeyRadar_itself_and_preserves_other_processes()
    {
        var processSource = new StubProcessSource(
        [
            new ProcessDescriptor(10, "KeyRadar", "KeyRadar.exe"),
            new ProcessDescriptor(20, "WeChat", "WeChat.exe"),
        ]);
        var windowSource = new StubWindowSource(
            [new WindowDescriptor((nint)200, 20)],
            (nint)200);
        var scanner = new RunningApplicationScanner(processSource, windowSource);

        var result = scanner.Scan(currentProcessId: 10);

        var application = Assert.Single(result);
        Assert.Equal(20, application.Process.Id);
        Assert.Equal(ApplicationPresence.Foreground, application.Presence);
    }

    private sealed class StubProcessSource(IReadOnlyList<ProcessDescriptor> processes) : IProcessSource
    {
        public IReadOnlyList<ProcessDescriptor> ReadProcesses() => processes;
    }

    private sealed class StubWindowSource(
        IReadOnlyList<WindowDescriptor> windows,
        nint foregroundWindow) : IWindowSource
    {
        public IReadOnlyList<WindowDescriptor> ReadWindows() => windows;

        public nint GetForegroundWindow() => foregroundWindow;
    }
}
