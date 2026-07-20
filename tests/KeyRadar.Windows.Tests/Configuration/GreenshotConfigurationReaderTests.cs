using KeyRadar.Rules;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Configuration;

namespace KeyRadar.Windows.Tests.Configuration;

public sealed class GreenshotConfigurationReaderTests
{
    [Fact]
    public async Task Reader_uses_only_known_hotkey_keys_and_ignores_other_ini_values()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllLinesAsync(path,
        [
            "[Core]",
            "RegionHotkey=Ctrl + Alt + PrintScreen",
            "WindowHotkey=Alt+PrintScreen",
            "ClipboardHotkey=None",
            "OutputFilePath=C:\\private",
        ], TestContext.Current.CancellationToken);
        try
        {
            var result = await new GreenshotConfigurationReader(() => path).ReadAsync(
                new ProcessDescriptor(42, "Greenshot", "Greenshot.exe"),
                Variant(),
                TestContext.Current.CancellationToken);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, item => item.Gesture.ToString() == "Ctrl+Alt+PrintScreen" && item.Function == "区域截图");
            Assert.DoesNotContain(result, item => item.Evidence.Contains("private", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ApplicationVariantRule Variant() => new(
        "greenshot", "default",
        new LocalizedText(new Dictionary<string, string> { ["en-US"] = "Greenshot" }),
        new ApplicationMatchRule(["Greenshot.exe"], [], null, [], null),
        []);
}
