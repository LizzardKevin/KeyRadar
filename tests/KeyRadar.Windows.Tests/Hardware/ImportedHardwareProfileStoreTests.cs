using System.Text.Json;
using KeyRadar.Windows.Hardware;

namespace KeyRadar.Windows.Tests.Hardware;

public sealed class ImportedHardwareProfileStoreTests
{
    [Fact]
    public async Task Imports_hotkey_mapping_and_removes_macro_text_and_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"KeyRadar-hardware-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.json");
        var stored = Path.Combine(root, "stored.json");
        await File.WriteAllTextAsync(source, """
            {
              "schemaVersion": 1,
              "softwareId": "logitech-g-hub",
              "deviceName": "Logitech G Keyboard",
              "profileName": "Desktop Profile",
              "isCurrent": true,
              "isOnboardMemory": false,
              "slot": null,
              "mappings": [
                { "physicalTrigger": "G2", "targetKind": "hotkey", "targetGesture": "Alt+A", "displayTarget": "ignored" },
                { "physicalTrigger": "G3", "targetKind": "macroSequence", "targetGesture": null, "displayTarget": "secret@example.com C:\\Users\\Secret\\macro.txt" },
                { "physicalTrigger": "G4", "targetKind": "launchApplication", "targetGesture": null, "displayTarget": "C:\\Private\\tool.exe" }
              ]
            }
            """, TestContext.Current.CancellationToken);

        try
        {
            var store = new ImportedHardwareProfileStore(stored);
            var result = await store.ImportAsync(source, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            var profile = Assert.IsType<HardwareProfileDescriptor>(result.Profile);
            Assert.True(profile.IsUserDeclared);
            Assert.Equal("Alt+A", profile.Mappings[0].DisplayTarget);
            Assert.Equal("Macro sequence (content hidden)", profile.Mappings[1].DisplayTarget);
            Assert.Equal("Launch application (path hidden)", profile.Mappings[2].DisplayTarget);
            var storedJson = await File.ReadAllTextAsync(stored, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("secret@example.com", storedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\Users", storedJson, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(store.Load());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_invalid_or_unbounded_imports()
    {
        var root = Path.Combine(Path.GetTempPath(), $"KeyRadar-hardware-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.json");
        await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            softwareId = "bad id with spaces",
            deviceName = "Keyboard",
            profileName = "Desktop",
            isCurrent = true,
            mappings = new[] { new { physicalTrigger = "G1", targetKind = "hotkey", targetGesture = "Alt+A" } },
        }), TestContext.Current.CancellationToken);

        try
        {
            var result = await new ImportedHardwareProfileStore(Path.Combine(root, "stored.json"))
                .ImportAsync(source, TestContext.Current.CancellationToken);
            Assert.False(result.IsSuccess);
            Assert.Equal("invalid-format", result.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
