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
    public void Unsigned_local_pack_can_be_imported_exported_and_removed_without_execution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"KeyRadar-LocalRules-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var candidate = Path.Combine(root, "candidate.krpack");
        var exported = Path.Combine(root, "exported.krpack");

        try
        {
            CreatePack(candidate, "local.1", key, unsigned: true);

            var imported = LocalRulePackStore.Import(candidate, root);
            var export = LocalRulePackStore.Export(root, exported);
            var removed = LocalRulePackStore.Remove(root);

            Assert.True(imported.IsSuccess, imported.Message);
            Assert.True(export.IsSuccess, export.Message);
            Assert.True(File.Exists(exported));
            Assert.True(removed.IsSuccess, removed.Message);
            Assert.False(File.Exists(Path.Combine(root, LocalRulePackStore.ActiveFileName)));
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

    private static void CreatePack(string path, string version, Key key, bool unsigned = false)
    {
        var ruleBytes = Encoding.UTF8.GetBytes("""
            {"schemaVersion":1,"applicationId":"wechat","displayName":"微信","executables":["WeChat.exe"],"shortcuts":[]}
            """);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            packId = "keyradar.test",
            version,
            files = new[]
            {
                new
                {
                    path = "rules/wechat.json",
                    sha256 = Convert.ToHexString(SHA256.HashData(ruleBytes)).ToLowerInvariant(),
                },
            },
        });
        var signature = SignatureAlgorithm.Ed25519.Sign(key, manifestBytes);

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", manifestBytes);
        WriteEntry(archive, "rules/wechat.json", ruleBytes);
        if (!unsigned)
        {
            WriteEntry(archive, "signature.ed25519", Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
        }
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var entryStream = entry.Open();
        entryStream.Write(content);
    }
}
