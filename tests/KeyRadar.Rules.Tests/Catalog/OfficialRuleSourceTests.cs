using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyRadar.Conflicts;
using KeyRadar.Rules.Packs;
using KeyRadar.Shortcuts;
using NSec.Cryptography;

namespace KeyRadar.Rules.Tests.Catalog;

public sealed class OfficialRuleSourceTests
{
    private static readonly Lazy<RulePack> SourcePack = new(LoadSourcePack);

    [Fact]
    public void Source_contains_fifty_apps_and_one_windows_system_rule()
    {
        var applications = SourcePack.Value.Applications;

        Assert.Equal(51, applications.Count);
        Assert.Equal(51, applications.Select(app => app.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(50, applications.Count(app => app.Id != "windows-system"));
        Assert.All(applications.Where(app => app.Id != "windows-system"), app => Assert.NotEmpty(app.ExecutableNames));

        var windows = Assert.Single(applications, app => app.Id == "windows-system");
        Assert.Empty(windows.ExecutableNames);
        Assert.NotEmpty(windows.Shortcuts);
        Assert.All(windows.Shortcuts, shortcut => Assert.Equal(ShortcutScope.WindowsSystem, shortcut.Scope));
    }

    [Fact]
    public void WeChat_Alt_A_is_a_global_screenshot_rule()
    {
        var wechat = Assert.Single(SourcePack.Value.Applications, app => app.Id == "wechat");
        var screenshot = Assert.Single(wechat.Shortcuts, shortcut =>
            shortcut.Gesture == ShortcutGesture.Parse("Alt+A"));

        Assert.Equal("截图", screenshot.Function);
        Assert.Equal(ShortcutScope.Global, screenshot.Scope);
        Assert.Equal(OwnershipConfidence.Configuration, screenshot.Confidence);
    }

    [Fact]
    public void Source_has_useful_shortcuts_for_at_least_thirty_five_apps()
    {
        var count = SourcePack.Value.Applications.Count(app =>
            app.Id != "windows-system" && app.Shortcuts.Count > 0);

        Assert.True(count >= 35, $"Only {count} applications contain shortcut rules.");
    }

    private static RulePack LoadSourcePack()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "rules"), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path: path, Content: File.ReadAllBytes(path)))
            .ToArray();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
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
