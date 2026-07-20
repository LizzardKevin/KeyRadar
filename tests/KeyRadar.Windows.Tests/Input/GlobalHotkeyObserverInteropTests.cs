using KeyRadar.Windows.Input;

namespace KeyRadar.Windows.Tests.Input;

public sealed class GlobalHotkeyObserverInteropTests
{
    [Fact]
    public void Start_ResolvesTheUnicodeWindowsHookEntryPoint()
    {
        using var observer = new GlobalHotkeyObserver();

        var exception = Record.Exception(observer.Start);

        Assert.Null(exception);
        Assert.True(observer.IsRunning);
    }
}
