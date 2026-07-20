using System.Text;
using KeyRadar.Rules;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Configuration;

namespace KeyRadar.Windows.Tests.Configuration;

public sealed class ShareXConfigurationReaderTests
{
    [Fact]
    public async Task Reader_extracts_only_explicit_hotkey_and_job_fields()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, """
            {"Hotkeys":[{"HotkeyInfo":{"Hotkey":393281,"Win":false},"TaskSettings":{"Job":14,"SecretPath":"C:\\private"}}]}
            """, Encoding.UTF8, TestContext.Current.CancellationToken);
        try
        {
            var reader = new ShareXConfigurationReader(() => path);

            var result = await reader.ReadAsync(
                new ProcessDescriptor(42, "ShareX", "ShareX.exe"),
                Variant(),
                TestContext.Current.CancellationToken);

            var hotkey = Assert.Single(result);
            Assert.Equal("Ctrl+Alt+A", hotkey.Gesture.ToString());
            Assert.Equal("矩形区域截图", hotkey.Function);
            Assert.DoesNotContain("private", hotkey.Evidence, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ApplicationVariantRule Variant() => new(
        "sharex", "default",
        new LocalizedText(new Dictionary<string, string> { ["en-US"] = "ShareX" }),
        new ApplicationMatchRule(["ShareX.exe"], [], null, [], null),
        []);
}
