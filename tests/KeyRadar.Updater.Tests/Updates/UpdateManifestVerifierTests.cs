using System.Security.Cryptography;
using System.Text;
using KeyRadar.Updater.Updates;
using NSec.Cryptography;

namespace KeyRadar.Updater.Tests.Updates;

public sealed class UpdateManifestVerifierTests
{
    [Fact]
    public void OfficialReleaseKey_IsRawEd25519PublicKey()
    {
        Assert.Equal(32, OfficialReleaseKey.GetBytes().Length);
    }

    [Fact]
    public void Verify_AcceptsAuthenticManifestAndMatchingAsset()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        });
        var manifestBytes = CreateManifestBytes(SHA256.HashData("release"u8));
        var signature = SignatureAlgorithm.Ed25519.Sign(key, manifestBytes);
        var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        var result = UpdateManifestVerifier.Verify(
            manifestBytes,
            signature,
            publicKey,
            "release"u8,
            new Uri("https://github.com/LizzardKevin/KeyRadar/"));

        Assert.True(result.IsValid);
        Assert.NotNull(result.Manifest);
        Assert.Equal("1.0.0", result.Manifest.Version);
        Assert.Equal("KeyRadar-v1.0.0-windows-x64.zip", result.Manifest.AssetName);
    }

    [Fact]
    public void Verify_RejectsManifestChangedAfterSigning()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var manifestBytes = CreateManifestBytes(SHA256.HashData("release"u8));
        var signature = SignatureAlgorithm.Ed25519.Sign(key, manifestBytes);
        manifestBytes[^2] ^= 1;

        var result = UpdateManifestVerifier.Verify(
            manifestBytes,
            signature,
            key.PublicKey.Export(KeyBlobFormat.RawPublicKey),
            "release"u8,
            new Uri("https://github.com/LizzardKevin/KeyRadar/"));

        Assert.False(result.IsValid);
        Assert.Equal(UpdateManifestValidationError.InvalidSignature, result.Error);
    }

    [Theory]
    [InlineData("http://github.com/LizzardKevin/KeyRadar/releases/download/v1.0.0/KeyRadar-v1.0.0-windows-x64.zip")]
    [InlineData("https://github.com/SomeoneElse/KeyRadar/releases/download/v1.0.0/KeyRadar-v1.0.0-windows-x64.zip")]
    [InlineData("https://evil.example/KeyRadar-v1.0.0-windows-x64.zip")]
    public void Verify_RejectsDownloadOutsidePinnedHttpsRepository(string downloadUrl)
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var manifestBytes = CreateManifestBytes(SHA256.HashData("release"u8), downloadUrl);
        var signature = SignatureAlgorithm.Ed25519.Sign(key, manifestBytes);

        var result = UpdateManifestVerifier.Verify(
            manifestBytes,
            signature,
            key.PublicKey.Export(KeyBlobFormat.RawPublicKey),
            "release"u8,
            new Uri("https://github.com/LizzardKevin/KeyRadar/"));

        Assert.False(result.IsValid);
        Assert.Equal(UpdateManifestValidationError.UntrustedDownload, result.Error);
    }

    [Fact]
    public void Verify_RejectsAssetWhoseHashDoesNotMatch()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var manifestBytes = CreateManifestBytes(SHA256.HashData("expected"u8));
        var signature = SignatureAlgorithm.Ed25519.Sign(key, manifestBytes);

        var result = UpdateManifestVerifier.Verify(
            manifestBytes,
            signature,
            key.PublicKey.Export(KeyBlobFormat.RawPublicKey),
            "tampered"u8,
            new Uri("https://github.com/LizzardKevin/KeyRadar/"));

        Assert.False(result.IsValid);
        Assert.Equal(UpdateManifestValidationError.AssetHashMismatch, result.Error);
    }

    private static byte[] CreateManifestBytes(ReadOnlySpan<byte> assetHash, string? downloadUrl = null)
    {
        downloadUrl ??= "https://github.com/LizzardKevin/KeyRadar/releases/download/v1.0.0/KeyRadar-v1.0.0-windows-x64.zip";
        var json = $$"""
            {
              "schemaVersion": 1,
              "version": "1.0.0",
              "assetName": "KeyRadar-v1.0.0-windows-x64.zip",
              "downloadUrl": "{{downloadUrl}}",
              "sha256": "{{Convert.ToHexString(assetHash).ToLowerInvariant()}}",
              "publishedAtUtc": "2026-07-20T00:00:00Z"
            }
            """;
        return Encoding.UTF8.GetBytes(json);
    }
}
