using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyRadar.Rules.Packs;
using NSec.Cryptography;

namespace KeyRadar.Rules.Tests.Packs;

public sealed class OfficialRulePackLoaderTests
{
    [Fact]
    public void Bundled_pack_is_used_on_first_launch()
    {
        using var context = TestContext.Create();
        context.CreateOfficialPack(context.BundledPath, "1.0.0");

        var result = OfficialRulePackLoader.Load(
            context.ActivePath,
            context.BundledPath,
            context.PublicKey);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("1.0.0", result.Pack!.Version);
    }

    [Fact]
    public void Invalid_active_pack_does_not_fall_back_to_bundled_pack()
    {
        using var context = TestContext.Create();
        Directory.CreateDirectory(Path.GetDirectoryName(context.ActivePath)!);
        File.WriteAllBytes(context.ActivePath, [0x01, 0x02, 0x03]);
        context.CreateOfficialPack(context.BundledPath, "1.0.0");

        var result = OfficialRulePackLoader.Load(
            context.ActivePath,
            context.BundledPath,
            context.PublicKey);

        Assert.False(result.IsSuccess);
        Assert.Contains("规则不可用", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_pack_reports_explicit_unavailable_state()
    {
        using var context = TestContext.Create();

        var result = OfficialRulePackLoader.Load(
            context.ActivePath,
            context.BundledPath,
            context.PublicKey);

        Assert.False(result.IsSuccess);
        Assert.Contains("规则不可用", result.Message, StringComparison.Ordinal);
        Assert.Contains("重新下载", result.Message, StringComparison.Ordinal);
    }

    private sealed class TestContext : IDisposable
    {
        private readonly Key _key;

        private TestContext(string root, Key key)
        {
            Root = root;
            _key = key;
            PublicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        }

        public string Root { get; }

        public string ActivePath => Path.Combine(Root, "data", "rules", RulePackStore.ActiveFileName);

        public string BundledPath => Path.Combine(Root, "KeyRadar-Rules-v1.0.0.krpack");

        public byte[] PublicKey { get; }

        public static TestContext Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"KeyRadar-RuleLoader-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
            {
                ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
            });
            return new TestContext(root, key);
        }

        public void CreateOfficialPack(string path, string version)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var rule = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":2,\"applicationId\":\"wechat\",\"variantId\":\"cn-desktop\",\"displayName\":{\"zh-CN\":\"微信\",\"en-US\":\"WeChat\"},\"match\":{\"executables\":[\"WeChat.exe\"],\"publishers\":[],\"versionRange\":null,\"packageFamilyNames\":[],\"distribution\":null},\"hotkeys\":[]}");
            var manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 2,
                packId = OfficialRulePack.PackId,
                version,
                files = new[]
                {
                    new
                    {
                        path = "rules/wechat-cn-desktop.json",
                        sha256 = Convert.ToHexString(SHA256.HashData(rule)).ToLowerInvariant(),
                    },
                },
            });
            var signature = SignatureAlgorithm.Ed25519.Sign(_key, manifest);

            using var output = File.Create(path);
            using var archive = new ZipArchive(output, ZipArchiveMode.Create);
            Write(archive, "manifest.json", manifest);
            Write(archive, "signature.ed25519", Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
            Write(archive, "rules/wechat-cn-desktop.json", rule);
        }

        public void Dispose()
        {
            _key.Dispose();
            Directory.Delete(Root, recursive: true);
        }

        private static void Write(ZipArchive archive, string path, byte[] content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
            using var stream = entry.Open();
            stream.Write(content);
        }
    }
}
