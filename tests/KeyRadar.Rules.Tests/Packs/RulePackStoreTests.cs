using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyRadar.Rules.Packs;
using NSec.Cryptography;

namespace KeyRadar.Rules.Tests.Packs;

public sealed class RulePackStoreTests
{
    [Fact]
    public void Activate_and_rollback_keep_a_verified_previous_pack()
    {
        var root = Path.Combine(Path.GetTempPath(), $"KeyRadar-Rules-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        });
        var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        try
        {
            var first = Path.Combine(root, "first.krpack");
            CreatePack(first, "1.0.0", key);
            var firstActivation = RulePackStore.Activate(first, root, publicKey);

            Assert.True(firstActivation.IsSuccess, firstActivation.Message);
            Assert.Equal("1.0.0", ReadVersion(Path.Combine(root, "active.krpack"), publicKey));

            var second = Path.Combine(root, "second.krpack");
            CreatePack(second, "2.0.0", key);
            var secondActivation = RulePackStore.Activate(second, root, publicKey);

            Assert.True(secondActivation.IsSuccess, secondActivation.Message);
            Assert.Equal("2.0.0", ReadVersion(Path.Combine(root, "active.krpack"), publicKey));
            Assert.Equal("1.0.0", ReadVersion(Path.Combine(root, "previous.krpack"), publicKey));

            var rollback = RulePackStore.Rollback(root, publicKey);

            Assert.True(rollback.IsSuccess, rollback.Message);
            Assert.Equal("1.0.0", ReadVersion(Path.Combine(root, "active.krpack"), publicKey));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rollback_rejects_a_signed_pack_with_the_wrong_identity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"KeyRadar-Rules-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        });

        try
        {
            var previous = Path.Combine(root, RulePackStore.PreviousFileName);
            CreatePack(previous, "1.0.0", key, "example.not-keyradar");

            var rollback = RulePackStore.Rollback(
                root,
                key.PublicKey.Export(KeyBlobFormat.RawPublicKey));

            Assert.False(rollback.IsSuccess);
            Assert.Contains("not the official", rollback.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(previous));
            Assert.False(File.Exists(Path.Combine(root, RulePackStore.ActiveFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ReadVersion(string path, byte[] publicKey)
    {
        using var stream = File.OpenRead(path);
        var result = RulePackReader.Read(stream, publicKey);
        Assert.True(result.IsSuccess, result.Message);
        return result.Pack!.Version;
    }

    private static void CreatePack(
        string path,
        string version,
        Key key,
        string packId = OfficialRulePack.PackId)
    {
        var ruleBytes = Encoding.UTF8.GetBytes("""
            {"schemaVersion":2,"applicationId":"wechat","variantId":"cn-desktop","displayName":{"zh-CN":"微信","en-US":"WeChat"},"match":{"executables":["WeChat.exe"],"publishers":[],"versionRange":null,"packageFamilyNames":[],"distribution":null},"hotkeys":[]}
            """);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            packId,
            version,
            files = new[]
            {
                new
                {
                    path = "rules/wechat-cn-desktop.json",
                    sha256 = Convert.ToHexString(SHA256.HashData(ruleBytes)).ToLowerInvariant(),
                },
            },
        });
        var signature = SignatureAlgorithm.Ed25519.Sign(key, manifestBytes);

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", manifestBytes);
        WriteEntry(archive, "rules/wechat-cn-desktop.json", ruleBytes);
        WriteEntry(archive, "signature.ed25519", Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var entryStream = entry.Open();
        entryStream.Write(content);
    }
}
