using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules.Packs;
using NSec.Cryptography;

namespace KeyRadar.Rules.Tests.Catalog;

public sealed class OfficialRuleSourceTests
{
    private static readonly Lazy<RulePack> SourcePack = new(LoadSourcePack);

    [Fact]
    public void Source_is_non_empty_and_application_variant_identities_are_unique()
    {
        var variants = SourcePack.Value.Variants;

        Assert.NotEmpty(variants);
        Assert.Equal(
            variants.Count,
            variants
                .Select(variant => $"{variant.ApplicationId}/{variant.VariantId}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(
            variants.Where(variant => variant.ApplicationId != "windows-system"),
            variant => Assert.NotEmpty(variant.Match.Executables));

        var windows = Assert.Single(variants, variant => variant.ApplicationId == "windows-system");
        Assert.Empty(windows.Match.Executables);
        Assert.NotEmpty(windows.Hotkeys);
        Assert.All(windows.Hotkeys, hotkey => Assert.Equal(HotkeyScope.WindowsSystem, hotkey.Scope));
    }

    [Fact]
    public void WeChat_Alt_A_is_a_global_screenshot_rule()
    {
        var wechat = Assert.Single(SourcePack.Value.Variants, variant =>
            variant.ApplicationId == "wechat" && variant.VariantId == "cn-desktop");
        var screenshot = Assert.Single(wechat.Hotkeys, hotkey =>
            hotkey.Gesture == HotkeyGesture.Parse("Alt+A"));

        Assert.Equal("截图", screenshot.Function.Resolve("zh-CN"));
        Assert.Equal(HotkeyScope.Global, screenshot.Scope);
        Assert.Equal(OwnershipConfidence.LocalConfiguration, screenshot.Confidence);
    }

    [Fact]
    public void Windows_system_rules_include_common_operating_system_hotkeys_with_Microsoft_sources()
    {
        var windows = Assert.Single(SourcePack.Value.Variants, variant => variant.ApplicationId == "windows-system");
        var expected = new[]
        {
            ("Alt+F4", "关闭当前活动窗口或应用", "Close the active window or app"),
            ("Alt+Tab", "切换窗口", "Switch windows"),
            ("Ctrl+Esc", "打开开始菜单", "Open Start"),
            ("Win+D", "显示或隐藏桌面", "Show or hide the desktop"),
            ("Win+E", "打开文件资源管理器", "Open File Explorer"),
            ("Win+I", "打开设置", "Open Settings"),
            ("Win+L", "锁定电脑", "Lock your PC"),
            ("Win+R", "打开“运行”对话框", "Open the Run dialog"),
            ("Win+Shift+S", "打开截图工具", "Open Snipping Tool"),
            ("Win+V", "打开剪贴板历史记录", "Open clipboard history"),
            ("Win+G", "打开 Xbox Game Bar", "Open Xbox Game Bar"),
        };

        foreach (var (gesture, chinese, english) in expected)
        {
            var hotkey = Assert.Single(windows.Hotkeys, item => item.Gesture == HotkeyGesture.Parse(gesture));
            Assert.Equal(HotkeyScope.WindowsSystem, hotkey.Scope);
            Assert.Equal(OwnershipConfidence.SystemKnown, hotkey.Confidence);
            Assert.Equal(chinese, hotkey.Function.Resolve("zh-CN"));
            Assert.Equal(english, hotkey.Function.Resolve("en-US"));
            Assert.All(hotkey.Sources, source => Assert.StartsWith("https://support.microsoft.com/", source, StringComparison.Ordinal));
            Assert.NotEmpty(hotkey.Sources);
        }
    }

    [Fact]
    public void Nvidia_overlay_variant_requires_the_specific_overlay_process_and_uses_official_source()
    {
        var nvidia = Assert.Single(SourcePack.Value.Variants, variant => variant.ApplicationId == "nvidia-app");

        Assert.Contains("NVIDIA Overlay.exe", nvidia.Match.Executables, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("nvcontainer.exe", nvidia.Match.Executables, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("NVIDIA Corporation", nvidia.Match.Publishers, StringComparer.OrdinalIgnoreCase);
        var overlay = Assert.Single(nvidia.Hotkeys, item => item.Gesture == HotkeyGesture.Parse("Alt+Z"));
        Assert.Equal(OwnershipConfidence.OfficialDefault, overlay.Confidence);
        Assert.Equal("打开 NVIDIA 覆盖层（默认热键，可修改）", overlay.Function.Resolve("zh-CN"));
        Assert.Equal("Open NVIDIA Overlay (default hotkey, configurable)", overlay.Function.Resolve("en-US"));
        Assert.Contains("https://www.nvidia.com/en-us/geforce/news/nvidia-app-download-and-features/", overlay.Sources);
    }

    private static RulePack LoadSourcePack()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "rules"), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path: path, Content: File.ReadAllBytes(path)))
            .ToArray();
        Assert.NotEmpty(files);

        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            packId = OfficialRulePack.PackId,
            version = "1.0.0",
            files = files.Select(file => new
            {
                path = $"rules/{Path.GetFileName(file.Path)}",
                sha256 = Convert.ToHexString(SHA256.HashData(file.Content)).ToLowerInvariant(),
            }),
        });
        using var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        });
        var signature = SignatureAlgorithm.Ed25519.Sign(key, manifest);

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "manifest.json", manifest);
            Write(
                archive,
                "signature.ed25519",
                Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
            foreach (var file in files)
            {
                Write(archive, $"rules/{Path.GetFileName(file.Path)}", file.Content);
            }
        }

        stream.Position = 0;
        var result = RulePackReader.Read(
            stream,
            key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        Assert.True(result.IsSuccess, result.Message);
        return result.Pack!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KeyRadar.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("KeyRadar repository root was not found.");
    }

    private static void Write(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(content);
    }
}
