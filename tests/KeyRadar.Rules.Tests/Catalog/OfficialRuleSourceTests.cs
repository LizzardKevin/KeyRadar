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
