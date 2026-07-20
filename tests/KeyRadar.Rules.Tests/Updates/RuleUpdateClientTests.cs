using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyRadar.Rules.Updates;
using NSec.Cryptography;

namespace KeyRadar.Rules.Tests.Updates;

public sealed class RuleUpdateClientTests
{
    [Fact]
    public async Task Check_requires_authentic_metadata_from_the_pinned_repository()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var manifest = ManifestBytes("1.2.0", SHA256.HashData("pack"u8));
        var signature = SignatureAlgorithm.Ed25519.Sign(key, manifest);
        using var httpClient = new HttpClient(new StubHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                    ? Encoding.ASCII.GetBytes(Convert.ToBase64String(signature))
                    : manifest),
            }));
        var client = new RuleUpdateClient(httpClient, key.PublicKey.Export(KeyBlobFormat.RawPublicKey));

        var result = await client.CheckAsync("1.0.0", TestContext.Current.CancellationToken);

        Assert.Equal(RuleUpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.2.0", result.Manifest?.Version);
    }

    [Fact]
    public async Task Download_requires_matching_hash_pack_identity_and_pack_signature()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var package = CreatePack("1.2.0", key);
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) }));
        var client = new RuleUpdateClient(httpClient, key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        var destination = Path.Combine(Path.GetTempPath(), $"KeyRadar-Rules-{Guid.NewGuid():N}.krpack");

        try
        {
            var result = await client.DownloadAsync(
                Manifest("1.2.0", SHA256.HashData(package)),
                destination,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess, result.Message);
            Assert.True(File.Exists(destination));
        }
        finally
        {
            File.Delete(destination);
        }
    }

    private static byte[] ManifestBytes(string version, byte[] hash) =>
        JsonSerializer.SerializeToUtf8Bytes(Manifest(version, hash));

    private static RuleUpdateManifest Manifest(string version, byte[] hash) => new(
        1,
        RuleUpdateClient.OfficialPackId,
        version,
        $"KeyRadar-Rules-v{version}.krpack",
        $"https://github.com/LizzardKevin/KeyRadar/releases/download/v{version}/KeyRadar-Rules-v{version}.krpack",
        Convert.ToHexString(hash).ToLowerInvariant(),
        DateTimeOffset.Parse("2026-07-20T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    private static byte[] CreatePack(string version, Key key)
    {
        var ruleBytes = """
            {"schemaVersion":2,"applicationId":"wechat","variantId":"cn-desktop","displayName":{"zh-CN":"微信","en-US":"WeChat"},"match":{"executables":["WeChat.exe"],"publishers":[],"versionRange":null,"packageFamilyNames":[],"distribution":null},"hotkeys":[]}
            """u8.ToArray();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            packId = RuleUpdateClient.OfficialPackId,
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
        var signature = SignatureAlgorithm.Ed25519.Sign(key, manifest);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "manifest.json", manifest);
            Write(archive, "rules/wechat-cn-desktop.json", ruleBytes);
            Write(archive, "signature.ed25519", Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
        }

        return stream.ToArray();
    }

    private static void Write(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
