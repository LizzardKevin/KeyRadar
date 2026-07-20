using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace KeyRadar.Updater.Updates;

public sealed class UpdateCheckClient
{
    private const int MaximumManifestBytes = 256 * 1024;
    private const int MaximumSignatureTextBytes = 1024;
    private const long MaximumAssetBytes = 512L * 1024 * 1024;
    private static readonly Uri TrustedRepository = new("https://github.com/LizzardKevin/KeyRadar/");
    private static readonly Uri LatestManifest = new("https://github.com/LizzardKevin/KeyRadar/releases/latest/download/keyradar-latest.json");
    private static readonly Uri LatestSignature = new("https://github.com/LizzardKevin/KeyRadar/releases/latest/download/keyradar-latest.json.sig");

    private readonly HttpClient _httpClient;
    private readonly byte[] _publicKey;

    public UpdateCheckClient(HttpClient httpClient, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(publicKey);
        _httpClient = httpClient;
        _publicKey = publicKey.ToArray();
    }

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        try
        {
            var manifestBytes = await GetBoundedAsync(LatestManifest, MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
            var signatureText = await GetBoundedAsync(LatestSignature, MaximumSignatureTextBytes, cancellationToken).ConfigureAwait(false);
            var signature = Convert.FromBase64String(Encoding.ASCII.GetString(signatureText).Trim());
            var validation = UpdateManifestVerifier.VerifyManifest(
                manifestBytes,
                signature,
                _publicKey,
                TrustedRepository);
            if (!validation.IsValid || validation.Manifest is null)
            {
                return UpdateCheckResult.Failure(validation.Message);
            }

            if (!Version.TryParse(validation.Manifest.Version, out var availableVersion))
            {
                return UpdateCheckResult.Failure("The signed update version is not supported.");
            }

            return availableVersion > currentVersion
                ? UpdateCheckResult.Available(validation.Manifest)
                : UpdateCheckResult.Current(validation.Manifest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.Failure("The update check timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or FormatException or InvalidDataException)
        {
            return UpdateCheckResult.Failure("Unable to verify updates. The current version was not changed.");
        }
    }

    public async Task<UpdateDownloadResult> DownloadAsync(
        UpdateManifest manifest,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var temporaryPath = Path.GetFullPath(destinationPath) + ".download";
        try
        {
            var downloadUri = new Uri(manifest.DownloadUrl, UriKind.Absolute);
            if (!IsTrustedAsset(downloadUri, manifest.AssetName) ||
                manifest.Sha256.Length != 64 ||
                !manifest.Sha256.All(Uri.IsHexDigit))
            {
                return UpdateDownloadResult.Failure("The update download location or hash is invalid.");
            }

            var destinationFullPath = Path.GetFullPath(destinationPath);
            if (File.Exists(destinationFullPath) || File.Exists(temporaryPath))
            {
                return UpdateDownloadResult.Failure("The update download destination already exists.");
            }

            var parent = Path.GetDirectoryName(destinationFullPath);
            if (parent is null)
            {
                return UpdateDownloadResult.Failure("The update download destination is invalid.");
            }

            Directory.CreateDirectory(parent);
            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("KeyRadar", "1.0"));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumAssetBytes)
            {
                return UpdateDownloadResult.Failure("The update package exceeds 512 MiB.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (var destination = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > MaximumAssetBytes)
                    {
                        return UpdateDownloadResult.Failure("The update package exceeds 512 MiB.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            byte[] actualHash;
            await using (var packageStream = File.OpenRead(temporaryPath))
            {
                actualHash = await SHA256.HashDataAsync(packageStream, cancellationToken).ConfigureAwait(false);
            }
            var expectedHash = Convert.FromHexString(manifest.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                return UpdateDownloadResult.Failure("The downloaded package failed SHA-256 verification.");
            }

            File.Move(temporaryPath, destinationFullPath);
            return UpdateDownloadResult.Success(destinationFullPath);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateDownloadResult.Failure("The update download timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or FormatException or UnauthorizedAccessException)
        {
            return UpdateDownloadResult.Failure("The update could not be downloaded safely.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<byte[]> GetBoundedAsync(Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("KeyRadar", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("The update metadata exceeds its size limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException("The update metadata exceeds its size limit.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static bool IsTrustedAsset(Uri uri, string assetName)
    {
        var repositoryPath = TrustedRepository.AbsolutePath.TrimEnd('/');
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               uri.Host.Equals(TrustedRepository.Host, StringComparison.OrdinalIgnoreCase) &&
               uri.Port == TrustedRepository.Port &&
               string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment) &&
               uri.AbsolutePath.StartsWith($"{repositoryPath}/releases/download/", StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.EndsWith($"/{Uri.EscapeDataString(assetName)}", StringComparison.Ordinal);
    }
}
