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
                    [new DiagnosticHotkey(
                        "Alt+A",
                        $"Capture for {Environment.UserName} from C:\\Users\\SecretUser\\capture.txt",
                        "global",
                        "configuration",
                        @"Imported from \\server\private\profile.json")]),
            ]) with
        {
            ProbeTelemetry =
            [
                new DiagnosticProbeTelemetry(
                    "F12",
                    "Occupied",
                    "RegisterHotKeyProbe",
                    DateTimeOffset.Parse("2026-07-21T01:02:03Z", System.Globalization.CultureInfo.InvariantCulture),
                    1409,
                    "Unknown",
                    "OccupiedOwnerUnknown"),
                new DiagnosticProbeTelemetry(
                    "BrowserBack",
                    "AvailableAtScanTime",
                    "RegisterHotKeyProbe",
                    DateTimeOffset.Parse("2026-07-21T01:02:04Z", System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    "Unknown",
                    "Unknown"),
            ],
        };

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
            Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"\\server\private", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Imported from [path]", json, StringComparison.Ordinal);

            Assert.Contains("\"schemaVersion\": 2", json, StringComparison.Ordinal);
            Assert.Contains("\"probeTelemetry\": [", json, StringComparison.Ordinal);
            Assert.Contains("\"gesture\": \"F12\"", json, StringComparison.Ordinal);
            Assert.Contains("\"availability\": \"Occupied\"", json, StringComparison.Ordinal);
            Assert.Contains("\"mechanism\": \"RegisterHotKeyProbe\"", json, StringComparison.Ordinal);
            Assert.Contains("\"ownerStatus\": \"Unknown\"", json, StringComparison.Ordinal);
            Assert.Contains("\"diagnosticStatus\": \"OccupiedOwnerUnknown\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"win32ErrorCode\": null", json, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(output);
        }
    }
}
