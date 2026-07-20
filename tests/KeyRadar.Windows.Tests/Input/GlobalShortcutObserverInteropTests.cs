using KeyRadar.Windows.Input;

namespace KeyRadar.Windows.Tests.Input;

public sealed class GlobalShortcutObserverInteropTests
{
    [Fact]
    public void Start_ResolvesTheUnicodeWindowsHookEntryPoint()
    {
        using var observer = new GlobalShortcutObserver();

        var exception = Record.Exception(observer.Start);

        Assert.Null(exception);
        Assert.True(observer.IsRunning);
    }
}
