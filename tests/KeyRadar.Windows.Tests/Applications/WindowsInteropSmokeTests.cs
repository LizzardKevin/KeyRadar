using KeyRadar.Shortcuts;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Foreground;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Applications;

public sealed class WindowsInteropSmokeTests
{
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
        var gesture = ShortcutGesture.Parse("Ctrl+Alt+F24");

        var exception = Record.Exception(() => api.TryRegister(0x4B52, gesture));
        api.Unregister(0x4B52);

        Assert.Null(exception);
    }
}
