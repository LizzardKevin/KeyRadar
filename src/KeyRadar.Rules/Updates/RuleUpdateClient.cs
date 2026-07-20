using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyRadar.Rules.Packs;
using NSec.Cryptography;

namespace KeyRadar.Rules.Updates;

public sealed class RuleUpdateClient
{
    public const string OfficialPackId = "io.github.lizzardkevin.keyradar.official";

    private const int MaximumManifestBytes = 256 * 1024;
    private const int MaximumSignatureTextBytes = 1024;
    private const long MaximumPackBytes = 16L * 1024 * 1024;
    private static readonly Uri TrustedRepository = new("https://github.com/LizzardKevin/KeyRadar/");
    private static readonly Uri LatestManifest = new("https://github.com/LizzardKevin/KeyRadar/releases/latest/download/keyradar-rules-latest.json");
    private static readonly Uri LatestSignature = new("https://github.com/LizzardKevin/KeyRadar/releases/latest/download/keyradar-rules-latest.json.sig");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly HttpClient _httpClient;
    private readonly byte[] _publicKey;

    public RuleUpdateClient(HttpClient httpClient, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(publicKey);
        _httpClient = httpClient;
        _publicKey = publicKey.ToArray();
    }

    public async Task<RuleUpdateCheckResult> CheckAsync(
        string? currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var manifestBytes = await GetBoundedAsync(
                LatestManifest,
                MaximumManifestBytes,
                cancellationToken).ConfigureAwait(false);
            var signatureText = await GetBoundedAsync(
                LatestSignature,
                MaximumSignatureTextBytes,
                cancellationToken).ConfigureAwait(false);
            var signature = Convert.FromBase64String(Encoding.ASCII.GetString(signatureText).Trim());
            var verification = VerifyManifest(manifestBytes, signature);
            if (verification is null)
            {
                return RuleUpdateCheckResult.Failure("The signed rule update metadata is invalid.");
            }

            var hasCurrent = Version.TryParse(currentVersion, out var current);
            var available = Version.Parse(verification.Version);
            return !hasCurrent || available > current
                ? RuleUpdateCheckResult.Available(verification)
                : RuleUpdateCheckResult.Current(verification);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RuleUpdateCheckResult.Failure("The rule update check timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or FormatException or JsonException or CryptographicException)
        {
            return RuleUpdateCheckResult.Failure("Unable to verify rule updates. Current rules were not changed.");
        }
    }

    public async Task<RuleUpdateDownloadResult> DownloadAsync(
        RuleUpdateManifest manifest,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var destinationFullPath = Path.GetFullPath(destinationPath);
        var temporaryPath = destinationFullPath + ".download";
        try
        {
            if (!IsValidManifest(manifest) || File.Exists(destinationFullPath) || File.Exists(temporaryPath))
            {
                return RuleUpdateDownloadResult.Failure("The rule download metadata or destination is invalid.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);
            using var request = CreateRequest(new Uri(manifest.DownloadUrl, UriKind.Absolute));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumPackBytes)
            {
                return RuleUpdateDownloadResult.Failure("The rule pack exceeds 16 MiB.");
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
                    if (total > MaximumPackBytes)
                    {
                        return RuleUpdateDownloadResult.Failure("The rule pack exceeds 16 MiB.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            var content = await File.ReadAllBytesAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            var expectedHash = Convert.FromHexString(manifest.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(content)))
            {
                return RuleUpdateDownloadResult.Failure("The rule pack failed SHA-256 verification.");
            }

            using (var packageStream = new MemoryStream(content, writable: false))
            {
                var package = RulePackReader.Read(packageStream, _publicKey);
                if (!package.IsSuccess ||
                    package.Pack!.PackId != manifest.PackId ||
                    package.Pack.Version != manifest.Version)
                {
                    return RuleUpdateDownloadResult.Failure("The downloaded rule pack failed signature or identity validation.");
                }
            }

            File.Move(temporaryPath, destinationFullPath);
            return RuleUpdateDownloadResult.Success(destinationFullPath);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RuleUpdateDownloadResult.Failure("The rule download timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or FormatException or UnauthorizedAccessException)
        {
            return RuleUpdateDownloadResult.Failure("The signed rule pack could not be downloaded safely.");
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private RuleUpdateManifest? VerifyManifest(byte[] manifestBytes, byte[] signature)
    {
        if (manifestBytes.Length is 0 or > MaximumManifestBytes ||
            signature.Length != 64 ||
            _publicKey.Length != 32)
        {
            return null;
        }

        var algorithm = SignatureAlgorithm.Ed25519;
        var publicKey = PublicKey.Import(algorithm, _publicKey, KeyBlobFormat.RawPublicKey);
        if (!algorithm.Verify(publicKey, manifestBytes, signature))
        {
            return null;
        }

        var manifest = JsonSerializer.Deserialize<RuleUpdateManifest>(manifestBytes, JsonOptions);
        return IsValidManifest(manifest) ? manifest : null;
    }

    private static bool IsValidManifest(RuleUpdateManifest? manifest)
    {
        if (manifest is not { SchemaVersion: 1, PackId: OfficialPackId } ||
            !Version.TryParse(manifest.Version, out var version) ||
            version.Build < 0 ||
            manifest.AssetName != $"KeyRadar-Rules-v{manifest.Version}.krpack" ||
            manifest.Sha256.Length != 64 ||
            !manifest.Sha256.All(Uri.IsHexDigit) ||
            manifest.PublishedAtUtc == default ||
            !Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri))
        {
            return false;
        }

        var repositoryPath = TrustedRepository.AbsolutePath.TrimEnd('/');
        return downloadUri.Scheme == Uri.UriSchemeHttps &&
               downloadUri.Host.Equals(TrustedRepository.Host, StringComparison.OrdinalIgnoreCase) &&
               downloadUri.Port == TrustedRepository.Port &&
               string.IsNullOrEmpty(downloadUri.Query) &&
               string.IsNullOrEmpty(downloadUri.Fragment) &&
               downloadUri.AbsolutePath.StartsWith($"{repositoryPath}/releases/download/", StringComparison.OrdinalIgnoreCase) &&
               downloadUri.AbsolutePath.EndsWith($"/{Uri.EscapeDataString(manifest.AssetName)}", StringComparison.Ordinal);
    }

    private async Task<byte[]> GetBoundedAsync(Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("Rule update metadata exceeds its size limit.");
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
                throw new InvalidDataException("Rule update metadata exceeds its size limit.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("KeyRadar", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return request;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
