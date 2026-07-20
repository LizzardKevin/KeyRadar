using System.Net;
using System.Security.Cryptography;
using System.Text;
using KeyRadar.Updater.Updates;
using NSec.Cryptography;

namespace KeyRadar.Updater.Tests.Updates;

public sealed class UpdateCheckClientTests
{
    [Fact]
    public async Task CheckAsync_ReturnsAvailableForNewerAuthenticRelease()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var manifest = CreateManifest("1.2.0");
        var signature = Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(key, manifest));
        using var client = CreateClient(manifest, Encoding.ASCII.GetBytes(signature));
        var sut = new UpdateCheckClient(client, key.PublicKey.Export(KeyBlobFormat.RawPublicKey));

        var result = await sut.CheckAsync(new Version(1, 1, 0), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.2.0", result.Manifest?.Version);
    }

    [Fact]
    public async Task CheckAsync_ReturnsUpToDateWhenVersionsMatch()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var manifest = CreateManifest("1.2.0");
        var signature = Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(key, manifest));
        using var client = CreateClient(manifest, Encoding.ASCII.GetBytes(signature));
        var sut = new UpdateCheckClient(client, key.PublicKey.Export(KeyBlobFormat.RawPublicKey));

        var result = await sut.CheckAsync(new Version(1, 2, 0), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task CheckAsync_FailsClosedForInvalidSignature()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var manifest = CreateManifest("1.2.0");
        using var client = CreateClient(manifest, Encoding.ASCII.GetBytes(Convert.ToBase64String(new byte[64])));
        var sut = new UpdateCheckClient(client, key.PublicKey.Export(KeyBlobFormat.RawPublicKey));

        var result = await sut.CheckAsync(new Version(1, 1, 0), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public async Task CheckAsync_ReportsNetworkFailureWithoutThrowing()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));
        var sut = new UpdateCheckClient(client, new byte[32]);

        var result = await sut.CheckAsync(new Version(1, 0, 0), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task DownloadAsync_WritesOnlyAssetMatchingSignedHash()
    {
        var asset = "release"u8.ToArray();
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(asset) }));
        var sut = new UpdateCheckClient(client, new byte[32]);
        var destination = Path.Combine(Path.GetTempPath(), $"KeyRadar-Download-{Guid.NewGuid():N}.zip");
        var manifest = CreateManifestModel(SHA256.HashData(asset));

        try
        {
            var result = await sut.DownloadAsync(manifest, destination, TestContext.Current.CancellationToken);

            Assert.True(result.IsValid);
            Assert.Equal(asset, File.ReadAllBytes(destination));
        }
        finally
        {
            File.Delete(destination);
        }
    }

    [Fact]
    public async Task DownloadAsync_DeletesPartialFileWhenHashDoesNotMatch()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("tampered"u8.ToArray()) }));
        var sut = new UpdateCheckClient(client, new byte[32]);
        var destination = Path.Combine(Path.GetTempPath(), $"KeyRadar-Download-{Guid.NewGuid():N}.zip");
        var manifest = CreateManifestModel(SHA256.HashData("expected"u8));

        var result = await sut.DownloadAsync(manifest, destination, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".download"));
    }

    private static HttpClient CreateClient(byte[] manifest, byte[] signature) =>
        new(new StubHandler(request =>
        {
            var content = request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal)
                ? signature
                : manifest;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        }));

    private static byte[] CreateManifest(string version) => Encoding.UTF8.GetBytes($$"""
        {
          "schemaVersion": 1,
          "version": "{{version}}",
          "assetName": "KeyRadar-v{{version}}-windows-x64.zip",
          "downloadUrl": "https://github.com/LizzardKevin/KeyRadar/releases/download/v{{version}}/KeyRadar-v{{version}}-windows-x64.zip",
          "sha256": "{{Convert.ToHexString(SHA256.HashData("release"u8)).ToLowerInvariant()}}",
          "publishedAtUtc": "2026-07-20T00:00:00Z"
        }
        """);

    private static UpdateManifest CreateManifestModel(ReadOnlySpan<byte> hash) => new(
        1,
        "1.0.0",
        "KeyRadar-v1.0.0-windows-x64.zip",
        "https://github.com/LizzardKevin/KeyRadar/releases/download/v1.0.0/KeyRadar-v1.0.0-windows-x64.zip",
        Convert.ToHexString(hash).ToLowerInvariant(),
        DateTimeOffset.Parse("2026-07-20T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
