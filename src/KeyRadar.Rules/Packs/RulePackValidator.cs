using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;

namespace KeyRadar.Rules.Packs;

public static class RulePackValidator
{
    private const int MaximumEntries = 512;
    private const long MaximumPackageContentBytes = 10 * 1024 * 1024;
    private const long MaximumManifestBytes = 256 * 1024;
    private const long MaximumRuleBytes = 512 * 1024;
    private const long MaximumSignatureBytes = 1024;

    public static RulePackValidationResult Validate(Stream packageStream, ReadOnlySpan<byte> publicKeyBytes)
        => ValidateInternal(packageStream, publicKeyBytes, requireSignature: true);

    private static RulePackValidationResult ValidateInternal(
        Stream packageStream,
        ReadOnlySpan<byte> publicKeyBytes,
        bool requireSignature)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        try
        {
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            var structuralResult = ValidateArchiveStructure(archive);
            if (!structuralResult.IsValid)
            {
                return structuralResult;
            }

            var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);
            if (!entries.TryGetValue("manifest.json", out var manifestEntry) ||
                requireSignature && !entries.ContainsKey("signature.ed25519"))
            {
                return RulePackValidationResult.Failure(
                    RulePackValidationError.MissingManifest,
                    "The package must contain manifest.json and signature.ed25519.");
            }

            var manifestBytes = ReadEntry(manifestEntry, MaximumManifestBytes);
            var manifest = JsonSerializer.Deserialize<RulePackManifest>(manifestBytes);
            if (!IsValidManifest(manifest))
            {
                return RulePackValidationResult.Failure(
                    RulePackValidationError.InvalidManifest,
                    "The rule-pack manifest is missing required version, identity, or file metadata.");
            }

            var declaredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest!.Files)
            {
                if (!IsAllowedRulePath(file.Path) || !declaredPaths.Add(file.Path))
                {
                    return RulePackValidationResult.Failure(
                        RulePackValidationError.DisallowedEntry,
                        "The manifest declares an unsafe, duplicate, or non-JSON rule path.");
                }

                if (!entries.TryGetValue(file.Path, out var ruleEntry))
                {
                    return RulePackValidationResult.Failure(
                        RulePackValidationError.MissingFile,
                        $"The declared rule '{file.Path}' is missing.");
                }

                var ruleBytes = ReadEntry(ruleEntry, MaximumRuleBytes);
                if (!HashMatches(ruleBytes, file.Sha256))
                {
                    return RulePackValidationResult.Failure(
                        RulePackValidationError.HashMismatch,
                        $"The SHA-256 hash for '{file.Path}' does not match the manifest.");
                }
            }

            var expectedEntryCount = manifest.Files.Count + (requireSignature ? 2 : 1);
            if (entries.Count != expectedEntryCount)
            {
                return RulePackValidationResult.Failure(
                    RulePackValidationError.UnexpectedFile,
                    "The package contains a file that is not declared in the manifest.");
            }

            if (requireSignature)
            {
                var signatureEntry = entries["signature.ed25519"];
                var signatureText = Encoding.ASCII.GetString(ReadEntry(signatureEntry, MaximumSignatureBytes)).Trim();
                var signature = Convert.FromBase64String(signatureText);
                var algorithm = SignatureAlgorithm.Ed25519;
                var publicKey = PublicKey.Import(algorithm, publicKeyBytes, KeyBlobFormat.RawPublicKey);
                if (!algorithm.Verify(publicKey, manifestBytes, signature))
                {
                    return RulePackValidationResult.Failure(
                        RulePackValidationError.InvalidSignature,
                        "The Ed25519 signature is not valid for this manifest.");
                }
            }

            return RulePackValidationResult.Success();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            JsonException or
            FormatException or
            CryptographicException or
            NotSupportedException)
        {
            return RulePackValidationResult.Failure(
                RulePackValidationError.MalformedArchive,
                "The package is malformed or uses an unsupported encoding.");
        }
    }

    private static RulePackValidationResult ValidateArchiveStructure(ZipArchive archive)
    {
        if (archive.Entries.Count is 0 or > MaximumEntries)
        {
            return RulePackValidationResult.Failure(
                RulePackValidationError.PackageTooLarge,
                "The package has no entries or exceeds the entry limit.");
        }

        long totalLength = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            totalLength += entry.Length;
            if (totalLength > MaximumPackageContentBytes)
            {
                return RulePackValidationResult.Failure(
                    RulePackValidationError.PackageTooLarge,
                    "The uncompressed package exceeds 10 MiB.");
            }

            if (!paths.Add(entry.FullName) || !IsAllowedEntry(entry.FullName))
            {
                return RulePackValidationResult.Failure(
                    RulePackValidationError.DisallowedEntry,
                    "The package contains an unsafe, duplicate, executable, or script entry.");
            }
        }

        return RulePackValidationResult.Success();
    }

    private static bool IsAllowedEntry(string path) =>
        IsSafeRelativePath(path) &&
        (path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
         path.Equals("signature.ed25519", StringComparison.OrdinalIgnoreCase) ||
         IsAllowedRulePath(path));

    private static bool IsAllowedRulePath(string path) =>
        IsSafeRelativePath(path) &&
        path.StartsWith("rules/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
        path.Length > "rules/.json".Length;

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.Contains('\\') ||
            path.Contains(':'))
        {
            return false;
        }

        return path
            .Split('/', StringSplitOptions.None)
            .All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsValidManifest(RulePackManifest? manifest) =>
        manifest is
        {
            SchemaVersion: 2,
            PackId.Length: > 0,
            Version.Length: > 0,
            Files: not null,
        };

    private static byte[] ReadEntry(ZipArchiveEntry entry, long maximumBytes)
    {
        if (entry.Length > maximumBytes)
        {
            throw new InvalidDataException("The archive entry exceeds its size limit.");
        }

        using var source = entry.Open();
        using var destination = new MemoryStream((int)entry.Length);
        source.CopyTo(destination);
        if (destination.Length > maximumBytes)
        {
            throw new InvalidDataException("The expanded archive entry exceeds its size limit.");
        }

        return destination.ToArray();
    }

    private static bool HashMatches(ReadOnlySpan<byte> content, string expectedHash)
    {
        if (expectedHash.Length != 64)
        {
            return false;
        }

        if (!expectedHash.All(Uri.IsHexDigit))
        {
            return false;
        }

        var expectedBytes = Convert.FromHexString(expectedHash);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(content), expectedBytes);
    }
}
