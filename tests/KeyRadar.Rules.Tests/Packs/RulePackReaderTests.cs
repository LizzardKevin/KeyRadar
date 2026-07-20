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
    public void Signed_pack_is_loaded_as_runtime_rules()
    {
        var package = CreatePack(("wechat", ValidRule("wechat", "微信", "WeChat.exe")));

        var result = RulePackReader.Read(package.Stream, package.PublicKey);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("keyradar.test", result.Pack!.PackId);
        Assert.Equal("1.2.3", result.Pack.Version);
        var application = Assert.Single(result.Pack.Applications);
        Assert.Equal("wechat", application.Id);
        Assert.Equal("微信", application.DisplayName);
        Assert.Equal("WeChat.exe", Assert.Single(application.ExecutableNames));
        var shortcut = Assert.Single(application.Shortcuts);
        Assert.Equal("Alt+A", shortcut.Gesture.ToString());
        Assert.Equal("截图", shortcut.Function);
        Assert.Equal(ShortcutScope.Global, shortcut.Scope);
        Assert.Equal(OwnershipConfidence.Configuration, shortcut.Confidence);
        Assert.Equal("https://example.test/wechat", Assert.Single(shortcut.Sources));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"applicationId\":\"wechat\",\"displayName\":\"微信\",\"executables\":[\"WeChat.exe\"],\"shortcuts\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"applicationId\":\"wechat\",\"displayName\":\"微信\",\"executables\":[\"../WeChat.exe\"],\"shortcuts\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"applicationId\":\"wechat\",\"displayName\":\"微信\",\"executables\":[\"WeChat.exe\"],\"shortcuts\":[{\"gesture\":\"Alt+A+B\",\"function\":\"截图\",\"scope\":\"global\",\"confidence\":\"configuration\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"applicationId\":\"wechat\",\"displayName\":\"微信\",\"executables\":[\"WeChat.exe\"],\"shortcuts\":[],\"unexpected\":true}")]
    public void Invalid_declarative_rule_is_rejected(string ruleJson)
    {
        var package = CreatePack(("wechat", Encoding.UTF8.GetBytes(ruleJson)));

        var result = RulePackReader.Read(package.Stream, package.PublicKey);

        Assert.False(result.IsSuccess);
        Assert.Equal(RulePackReadError.InvalidRule, result.Error);
    }

    [Fact]
    public void Duplicate_application_ids_are_rejected()
    {
        var rule = ValidRule("wechat", "微信", "WeChat.exe");
        var package = CreatePack(("wechat", rule), ("wechat-copy", rule));

        var result = RulePackReader.Read(package.Stream, package.PublicKey);

        Assert.False(result.IsSuccess);
        Assert.Equal(RulePackReadError.DuplicateApplication, result.Error);
    }

    [Fact]
    public void Unsigned_local_pack_is_loaded_and_every_shortcut_is_visibly_marked()
    {
        var package = CreatePack(
            true,
            ("wechat", ValidRule("wechat", "本地微信规则", "WeChat.exe")));

        var result = RulePackReader.ReadUnsignedLocal(package.Stream);

        Assert.True(result.IsSuccess, result.Message);
        var shortcut = Assert.Single(Assert.Single(result.Pack!.Applications).Shortcuts);
        Assert.Equal(RuleOrigin.LocalUnsigned, shortcut.Origin);
    }

    private static byte[] ValidRule(string id, string displayName, string executable) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            applicationId = id,
            displayName,
            executables = new[] { executable },
            shortcuts = new[]
            {
                new
                {
                    gesture = "Alt+A",
                    function = "截图",
                    scope = "global",
                    confidence = "configuration",
                    sources = new[] { "https://example.test/wechat" },
                },
            },
        });

    private static TestPack CreatePack(params (string FileName, byte[] Content)[] rules) =>
        CreatePack(false, rules);

    private static TestPack CreatePack(bool unsigned, params (string FileName, byte[] Content)[] rules)
    {
        var files = rules.Select(rule => new
        {
            path = $"rules/{rule.FileName}.json",
            sha256 = Convert.ToHexString(SHA256.HashData(rule.Content)).ToLowerInvariant(),
        });
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
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

            if (!unsigned)
            {
                WriteEntry(archive, "signature.ed25519", Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
            }
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
