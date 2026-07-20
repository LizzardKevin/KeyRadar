using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using NSec.Cryptography;

namespace KeyRadar.Updater.Updates;

public static partial class UpdateManifestVerifier
{
    private const int MaximumManifestBytes = 256 * 1024;
    private const int Ed25519PublicKeyBytes = 32;
    private const int Ed25519SignatureBytes = 64;

    public static UpdateManifestValidationResult Verify(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureBytes,
        ReadOnlySpan<byte> publicKeyBytes,
        ReadOnlySpan<byte> assetBytes,
        Uri trustedRepository)
    {
        ArgumentNullException.ThrowIfNull(trustedRepository);

        if (manifestBytes.IsEmpty || manifestBytes.Length > MaximumManifestBytes ||
            signatureBytes.Length != Ed25519SignatureBytes ||
            publicKeyBytes.Length != Ed25519PublicKeyBytes ||
            !VerifySignature(manifestBytes, signatureBytes, publicKeyBytes))
        {
            return UpdateManifestValidationResult.Failure(
                UpdateManifestValidationError.InvalidSignature,
                "The update manifest signature is invalid.");
        }

        UpdateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes);
        }
        catch (JsonException)
        {
            return UpdateManifestValidationResult.Failure(
                UpdateManifestValidationError.MalformedManifest,
                "The update manifest is not valid JSON.");
        }

        if (!IsValidManifest(manifest))
        {
            return UpdateManifestValidationResult.Failure(
                UpdateManifestValidationError.InvalidManifest,
                "The update manifest contains invalid version or asset metadata.");
        }

        var downloadUri = new Uri(manifest!.DownloadUrl, UriKind.Absolute);
        if (!IsTrustedDownload(downloadUri, trustedRepository, manifest.AssetName))
        {
            return UpdateManifestValidationResult.Failure(
                UpdateManifestValidationError.UntrustedDownload,
                "The update download is outside the pinned HTTPS repository.");
        }

        var expectedHash = Convert.FromHexString(manifest.Sha256);
        var actualHash = SHA256.HashData(assetBytes);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
        {
            return UpdateManifestValidationResult.Failure(
                UpdateManifestValidationError.AssetHashMismatch,
                "The downloaded update does not match the signed SHA-256 hash.");
        }

        return UpdateManifestValidationResult.Success(manifest);
    }

    private static bool VerifySignature(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureBytes,
        ReadOnlySpan<byte> publicKeyBytes)
    {
        try
        {
            var algorithm = SignatureAlgorithm.Ed25519;
            var publicKey = PublicKey.Import(algorithm, publicKeyBytes, KeyBlobFormat.RawPublicKey);
            return algorithm.Verify(publicKey, manifestBytes, signatureBytes);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool IsValidManifest(UpdateManifest? manifest)
    {
        if (manifest is not { SchemaVersion: 1 } ||
            !SemanticVersionRegex().IsMatch(manifest.Version) ||
            !AssetNameRegex().IsMatch(manifest.AssetName) ||
            manifest.PublishedAtUtc == default ||
            manifest.Sha256.Length != 64 ||
            !manifest.Sha256.All(Uri.IsHexDigit))
        {
            return false;
        }

        return Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out _);
    }

    private static bool IsTrustedDownload(Uri downloadUri, Uri trustedRepository, string assetName)
    {
        if (!downloadUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !trustedRepository.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !downloadUri.Host.Equals(trustedRepository.Host, StringComparison.OrdinalIgnoreCase) ||
            downloadUri.Port != trustedRepository.Port ||
            !string.IsNullOrEmpty(downloadUri.Query) ||
            !string.IsNullOrEmpty(downloadUri.Fragment))
        {
            return false;
        }

        var repositoryPath = trustedRepository.AbsolutePath.TrimEnd('/');
        var expectedPrefix = $"{repositoryPath}/releases/download/";
        return downloadUri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
               downloadUri.AbsolutePath.EndsWith($"/{Uri.EscapeDataString(assetName)}", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionRegex();

    [GeneratedRegex(@"^KeyRadar-v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)-windows-x64\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex AssetNameRegex();
}
