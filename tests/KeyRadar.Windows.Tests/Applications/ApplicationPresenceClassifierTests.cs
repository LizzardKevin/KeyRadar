using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Tests.Applications;

public sealed class ApplicationPresenceClassifierTests
{
    [Fact]
    public void Process_owning_the_foreground_window_is_foreground()
    {
        var processes = new[]
        {
            new ProcessDescriptor(10, "WeChat", "WeChat.exe"),
            new ProcessDescriptor(20, "Chrome", "chrome.exe"),
        };
        var windows = new[]
        {
            new WindowDescriptor((nint)100, 10),
            new WindowDescriptor((nint)200, 20),
        };

        var result = ApplicationPresenceClassifier.Classify(processes, windows, (nint)200);

        Assert.Equal(ApplicationPresence.Background, Assert.Single(result, item => item.Process.Id == 10).Presence);
        Assert.Equal(ApplicationPresence.Foreground, Assert.Single(result, item => item.Process.Id == 20).Presence);
    }

    [Fact]
    public void Headless_processes_are_kept_as_background_applications()
    {
        var processes = new[] { new ProcessDescriptor(10, "PowerToys", "PowerToys.exe") };

        var result = ApplicationPresenceClassifier.Classify(processes, [], nint.Zero);

        Assert.Equal(ApplicationPresence.Background, Assert.Single(result).Presence);
    }

    [Fact]
    public void No_foreground_window_produces_only_background_applications()
    {
        var processes = new[] { new ProcessDescriptor(10, "WeChat", "WeChat.exe") };
        var windows = new[] { new WindowDescriptor((nint)100, 10) };

        var result = ApplicationPresenceClassifier.Classify(processes, windows, nint.Zero);

        Assert.DoesNotContain(result, item => item.Presence == ApplicationPresence.Foreground);
    }
}
