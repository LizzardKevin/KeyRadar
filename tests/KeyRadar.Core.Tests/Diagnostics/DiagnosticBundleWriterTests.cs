using System.IO.Compression;
using System.Text;
using KeyRadar.Diagnostics;

namespace KeyRadar.Core.Tests.Diagnostics;

public sealed class DiagnosticBundleWriterTests
{
    [Fact]
    public void Bundle_contains_only_bounded_redacted_diagnostic_fields()
    {
        var output = Path.Combine(Path.GetTempPath(), $"KeyRadar-Diagnostics-{Guid.NewGuid():N}.zip");
        var report = new DiagnosticReport(
            "1.0.0",
            "Windows 11",
            DateTimeOffset.Parse("2026-07-20T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            [
                new DiagnosticApplication(
                    "wechat",
                    "微信",
                    @"C:\Users\SecretUser\WeChat.exe",
                    "4.0.0",
                    "Tencent",
                    "x64",
                    "standard",
                    "foreground",
                    [new DiagnosticHotkey("Alt+A", "截图", "global", "configuration", "official-rule")]),
            ]);

        try
        {
            DiagnosticBundleWriter.Write(output, report);

            using var archive = ZipFile.OpenRead(output);
            Assert.Equal(["diagnostics.json", "README.txt"], archive.Entries.Select(entry => entry.FullName).Order().ToArray());
            var jsonEntry = Assert.Single(archive.Entries, entry => entry.FullName == "diagnostics.json");
            using var reader = new StreamReader(jsonEntry.Open(), Encoding.UTF8);
            var json = reader.ReadToEnd();
            Assert.Contains("WeChat.exe", json);
            Assert.DoesNotContain("SecretUser", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\Users", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
