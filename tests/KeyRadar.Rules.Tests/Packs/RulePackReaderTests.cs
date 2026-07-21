using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyRadar.Conflicts;
using KeyRadar.Rules.Packs;
using NSec.Cryptography;

namespace KeyRadar.Rules.Tests.Packs;

public sealed class RulePackReaderTests
{
    [Fact]
    public void Signed_v2_pack_is_loaded_as_application_variants()
    {
        var package = CreatePack(("wechat-cn-desktop", ValidRule(
            "wechat",
            "cn-desktop",
            "微信",
            "WeChat",
            "WeChat.exe")));

        var result = RulePackReader.Read(package.Stream, package.PublicKey);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("keyradar.test", result.Pack!.PackId);
        Assert.Equal("1.2.3", result.Pack.Version);
        var application = Assert.Single(result.Pack.Variants);
        Assert.Equal("wechat", application.ApplicationId);
        Assert.Equal("cn-desktop", application.VariantId);
        Assert.Equal("微信", application.DisplayName.Resolve("zh-CN"));
        Assert.Equal("WeChat", application.DisplayName.Resolve("en-US"));
        Assert.Equal("WeChat.exe", Assert.Single(application.Match.Executables));
        var hotkey = Assert.Single(application.Hotkeys);
        Assert.Equal("Alt+A", hotkey.Gesture.ToString());
        Assert.Equal("截图", hotkey.Function.Resolve("zh-CN"));
        Assert.Equal(HotkeyScope.Global, hotkey.Scope);
        Assert.Equal(OwnershipConfidence.LocalConfiguration, hotkey.Confidence);
        Assert.Equal("https://example.test/wechat", Assert.Single(hotkey.Sources));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"applicationId\":\"wechat\",\"variantId\":\"cn-desktop\",\"displayName\":{\"zh-CN\":\"微信\"},\"match\":{\"executables\":[\"WeChat.exe\"],\"publishers\":[],\"versionRange\":null,\"packageFamilyNames\":[],\"distribution\":null},\"hotkeys\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"applicationId\":\"wechat\",\"variantId\":\"cn-desktop\",\"displayName\":{\"zh-CN\":\"微信\"},\"match\":{\"executables\":[\"../WeChat.exe\"],\"publishers\":[],\"versionRange\":null,\"packageFamilyNames\":[],\"distribution\":null},\"hotkeys\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"applicationId\":\"wechat\",\"variantId\":\"cn-desktop\",\"displayName\":{\"zh-CN\":\"微信\"},\"match\":{\"executables\":[\"WeChat.exe\"],\"publishers\":[],\"versionRange\":null,\"packageFamilyNames\":[],\"distribution\":null},\"hotkeys\":[{\"gesture\":\"Alt+A+B\",\"function\":{\"zh-CN\":\"截图\"},\"scope\":\"global\",\"confidence\":\"configuration\"}]}")]
    [InlineData("{\"schemaVersion\":2,\"applicationId\":\"wechat\",\"variantId\":\"cn-desktop\",\"displayName\":{\"zh-CN\":\"微信\"},\"match\":{\"executables\":[\"WeChat.exe\"],\"publishers\":[],\"versionRange\":\"System.IO.File.Delete('*')\",\"packageFamilyNames\":[],\"distribution\":null},\"hotkeys\":[]}")]
    public void Invalid_declarative_rule_is_rejected(string ruleJson)
    {
        var package = CreatePack(("wechat-cn-desktop", Encoding.UTF8.GetBytes(ruleJson)));

        var result = RulePackReader.Read(package.Stream, package.PublicKey);

        Assert.False(result.IsSuccess);
        Assert.Equal(RulePackReadError.InvalidRule, result.Error);
    }

    [Fact]
    public void Duplicate_application_variant_identity_is_rejected()
    {
        var rule = ValidRule("wechat", "cn-desktop", "微信", "WeChat", "WeChat.exe");
        var package = CreatePack(("wechat-cn-desktop", rule), ("wechat-cn-desktop-copy", rule));

        var result = RulePackReader.Read(package.Stream, package.PublicKey);

        Assert.False(result.IsSuccess);
        Assert.Equal(RulePackReadError.DuplicateApplicationVariant, result.Error);
    }

    [Fact]
    public void Same_application_can_have_multiple_variants()
    {
        var package = CreatePack(
            ("wechat-cn-desktop", ValidRule("wechat", "cn-desktop", "微信", "WeChat", "WeChat.exe")),
            ("wechat-international", ValidRule("wechat", "international", "WeChat", "WeChat", "WeChat.exe")));

        var result = RulePackReader.Read(package.Stream, package.PublicKey);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, result.Pack!.Variants.Count);
    }

    [Fact]
    public void Signed_pack_loads_bounded_declarative_configuration_sources()
    {
        var package = CreatePack(("nvidia-overlay", ConfigurationRule()));

        var result = RulePackReader.Read(package.Stream, package.PublicKey);

        Assert.True(result.IsSuccess, result.Message);
        var source = Assert.Single(result.Pack!.Variants[0].ConfigurationSources);
        Assert.True(result.Pack.Variants[0].IsConfigurationReadAuthorized);
        Assert.Equal(ConfigurationSourceRoot.LocalAppData, source.Root);
        Assert.Equal(ConfigurationGestureDecoder.VirtualKeyArray, source.Entries[0].Decoder);
    }

    [Fact]
    public void Unsigned_local_pack_cannot_authorize_configuration_sources()
    {
        using var stream = new MemoryStream();
        LocalRulePackWriter.Write(stream, [new ApplicationVariantRule(
            "nvidia-app", "overlay", new LocalizedText(new Dictionary<string, string> { ["en-US"] = "NVIDIA" }),
            new ApplicationMatchRule(["NVIDIA Overlay.exe"], [], null, [], null), [])
        {
            ConfigurationSources = [new ConfigurationSourceRule("nvidia", ConfigurationSourceRoot.LocalAppData,
                "NVIDIA Corporation/NVIDIA Overlay/ShareSettings.json", ConfigurationSourceFormat.Json, 4096,
                [new ConfigurationEntryRule("performance-overlay-toggle", "settings.shortcuts.PMOCOverlay",
                    ConfigurationGestureDecoder.VirtualKeyArray, new LocalizedText(new Dictionary<string, string> { ["en-US"] = "Overlay" }), HotkeyScope.Global)])],
        }]);
        stream.Position = 0;

        var result = RulePackReader.ReadLocal(stream);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Empty(result.Pack!.Variants[0].ConfigurationSources);
        Assert.False(result.Pack.Variants[0].IsConfigurationReadAuthorized);
    }

    private static byte[] ConfigurationRule() => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = 2, applicationId = "nvidia-app", variantId = "overlay",
        displayName = new Dictionary<string, string> { ["en-US"] = "NVIDIA App" },
        match = new { executables = new[] { "NVIDIA Overlay.exe" }, publishers = Array.Empty<string>(), versionRange = (string?)null, packageFamilyNames = Array.Empty<string>(), distribution = (string?)null },
        hotkeys = Array.Empty<object>(),
        configurationSources = new[] { new {
            sourceId = "nvidia-shortcuts", root = "localAppData", relativePath = "NVIDIA Corporation/NVIDIA Overlay/ShareSettings.json",
            format = "json", maxBytes = 4096,
            entries = new[] { new { commandId = "performance-overlay-toggle", gestureSelector = "settings.shortcuts.PMOCOverlay", decoder = "virtualKeyArray", function = new Dictionary<string, string> { ["en-US"] = "Performance overlay" }, scope = "global" } },
        } },
    });

    private static byte[] ValidRule(
        string applicationId,
        string variantId,
        string chineseName,
        string englishName,
        string executable) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            applicationId,
            variantId,
            displayName = new Dictionary<string, string>
            {
                ["zh-CN"] = chineseName,
                ["en-US"] = englishName,
            },
            match = new
            {
                executables = new[] { executable },
                publishers = new[] { "Tencent" },
                versionRange = ">=4.0 <5.0",
                packageFamilyNames = Array.Empty<string>(),
                distribution = "official",
            },
            hotkeys = new[]
            {
                new
                {
                    gesture = "Alt+A",
                    function = new Dictionary<string, string>
                    {
                        ["zh-CN"] = "截图",
                        ["en-US"] = "Screenshot",
                    },
                    scope = "global",
                    confidence = "configuration",
                    sources = new[] { "https://example.test/wechat" },
                },
            },
        });

    private static TestPack CreatePack(params (string FileName, byte[] Content)[] rules)
    {
        var files = rules.Select(rule => new
        {
            path = $"rules/{rule.FileName}.json",
            sha256 = Convert.ToHexString(SHA256.HashData(rule.Content)).ToLowerInvariant(),
        });
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            packId = "keyradar.test",
            version = "1.2.3",
            files,
        });

        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        });
        var signature = algorithm.Sign(key, manifestBytes);
        var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "manifest.json", manifestBytes);
            foreach (var rule in rules)
            {
                WriteEntry(archive, $"rules/{rule.FileName}.json", rule.Content);
            }

            WriteEntry(archive, "signature.ed25519", Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
        }

        stream.Position = 0;
        return new TestPack(stream, publicKey);
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var entryStream = entry.Open();
        entryStream.Write(content);
    }

    private sealed record TestPack(MemoryStream Stream, byte[] PublicKey);
}
