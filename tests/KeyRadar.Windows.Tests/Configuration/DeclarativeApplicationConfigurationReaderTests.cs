using System.Text;
using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Configuration;

namespace KeyRadar.Windows.Tests.Configuration;

public sealed class DeclarativeApplicationConfigurationReaderTests
{
    [Fact]
    public async Task Json_virtual_key_array_is_decoded_from_a_declared_selector()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "ShareSettings.json"),
                "{\"settings\":{\"shortcuts\":{\"PMOCOverlay\":[18,73]}}}", Encoding.UTF8,
                TestContext.Current.CancellationToken);

            var result = await Reader(root).ReadAsync(Process(), NvidiaVariant(), TestContext.Current.CancellationToken);

            var hotkey = Assert.Single(result);
            Assert.Equal("Alt+I", hotkey.Gesture.ToString());
            Assert.Equal("性能统计叠加层", hotkey.Function);
            Assert.Equal("performance-overlay-toggle", hotkey.CommandId);
            Assert.DoesNotContain(root, hotkey.Evidence, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Json_collection_decodes_winforms_hotkey_and_declared_value_mapping()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "HotkeysConfig.json"),
                "{\"Hotkeys\":[{\"HotkeyInfo\":{\"Hotkey\":393281,\"Win\":false},\"TaskSettings\":{\"Job\":14,\"Secret\":\"private\"}}]}", Encoding.UTF8,
                TestContext.Current.CancellationToken);

            var hotkey = Assert.Single(await Reader(root).ReadAsync(Process(), ShareXVariant(), TestContext.Current.CancellationToken));

            Assert.Equal("Ctrl+Alt+A", hotkey.Gesture.ToString());
            Assert.Equal("矩形区域截图", hotkey.Function);
            Assert.Equal("capture-region", hotkey.CommandId);
            Assert.DoesNotContain("private", hotkey.Evidence, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Ini_gesture_string_reads_only_declared_key()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(root, "greenshot.ini"),
            ["RegionHotkey=Ctrl + Alt + PrintScreen", "OutputFilePath=C:\\private"], TestContext.Current.CancellationToken);

            var hotkey = Assert.Single(await Reader(root).ReadAsync(Process(), GreenshotVariant(), TestContext.Current.CancellationToken));

            Assert.Equal("Ctrl+Alt+PrintScreen", hotkey.Gesture.ToString());
            Assert.Equal("区域截图", hotkey.Function);
            Assert.DoesNotContain("private", hotkey.Evidence, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Unauthorized_variant_never_reads_configuration_sources()
    {
        var root = CreateRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "ShareSettings.json"), "{\"settings\":{\"shortcuts\":{\"PMOCOverlay\":[18,73]}}}", TestContext.Current.CancellationToken);

            var result = await Reader(root).ReadAsync(Process(), NvidiaVariant() with { IsConfigurationReadAuthorized = false }, TestContext.Current.CancellationToken);

            Assert.Empty(result);
        }
        finally { Directory.Delete(root, true); }
    }

    private static DeclarativeApplicationConfigurationReader Reader(string root) =>
        new(sourceRoot => root);

    private static ProcessDescriptor Process() => new(42, "NVIDIA App", "NVIDIA Overlay.exe");

    private static ApplicationVariantRule NvidiaVariant() => Variant("nvidia-app", "overlay", [
        new ConfigurationSourceRule("nvidia-shortcuts", ConfigurationSourceRoot.LocalAppData, "ShareSettings.json", ConfigurationSourceFormat.Json, 4096,
        [new ConfigurationEntryRule("performance-overlay-toggle", "settings.shortcuts.PMOCOverlay", ConfigurationGestureDecoder.VirtualKeyArray, Text("性能统计叠加层"), HotkeyScope.Global)])
    ]);

    private static ApplicationVariantRule ShareXVariant() => Variant("sharex", "default", [
        new ConfigurationSourceRule("sharex-hotkeys", ConfigurationSourceRoot.Documents, "HotkeysConfig.json", ConfigurationSourceFormat.Json, 4096,
        [new ConfigurationEntryRule("custom-hotkey", "HotkeyInfo.Hotkey", ConfigurationGestureDecoder.WinFormsHotkey, Text("自定义任务"), HotkeyScope.Global)
        {
            CollectionSelector = "Hotkeys", WinSelector = "HotkeyInfo.Win", FunctionSelector = "TaskSettings.Job",
            FunctionValues = new Dictionary<string, LocalizedText> { ["14"] = Text("矩形区域截图") },
            CommandIdValues = new Dictionary<string, string> { ["14"] = "capture-region" },
        }])
    ]);

    private static ApplicationVariantRule GreenshotVariant() => Variant("greenshot", "default", [
        new ConfigurationSourceRule("greenshot-hotkeys", ConfigurationSourceRoot.RoamingAppData, "greenshot.ini", ConfigurationSourceFormat.Ini, 4096,
        [new ConfigurationEntryRule("capture-region", "RegionHotkey", ConfigurationGestureDecoder.GestureString, Text("区域截图"), HotkeyScope.Global)])
    ]);

    private static ApplicationVariantRule Variant(string id, string variant, IReadOnlyList<ConfigurationSourceRule> sources) => new(
        id, variant, Text(id), new ApplicationMatchRule(["NVIDIA Overlay.exe"], [], null, [], null), [])
    { ConfigurationSources = sources, IsConfigurationReadAuthorized = true };

    private static LocalizedText Text(string value) => new(new Dictionary<string, string> { ["zh-CN"] = value });
    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"KeyRadar-Config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
