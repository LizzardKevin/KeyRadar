using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyRadar.Rules.Packs;
using NSec.Cryptography;

namespace KeyRadar.ReleaseTool;

internal static class Program
{
    private static readonly DateTimeOffset DeterministicTimestamp =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static int Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            var version = Required(options, "version");
            var stagingDirectory = ExistingDirectory(Required(options, "staging"));
            var outputDirectory = Path.GetFullPath(Required(options, "output"));
            var rulesDirectory = ExistingDirectory(Required(options, "rules"));
            var publishedAt = DateTimeOffset.Parse(
                Required(options, "published-at"),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal);
            var privateKeyText = Environment.GetEnvironmentVariable("KEYRADAR_ED25519_PRIVATE_KEY");
            if (string.IsNullOrWhiteSpace(privateKeyText))
            {
                throw new InvalidOperationException("KEYRADAR_ED25519_PRIVATE_KEY is required.");
            }

            Directory.CreateDirectory(outputDirectory);
            using var signingKey = ImportSigningKey(privateKeyText);
            var ruleAssetPath = BuildRuleRelease(
                version,
                rulesDirectory,
                outputDirectory,
                publishedAt,
                signingKey);
            File.Copy(
                ruleAssetPath,
                Path.Combine(stagingDirectory, Path.GetFileName(ruleAssetPath)),
                overwrite: true);
            BuildApplicationRelease(version, stagingDirectory, outputDirectory, publishedAt, signingKey);
            File.WriteAllText(
                Path.Combine(outputDirectory, "keyradar-ed25519-public-key.txt"),
                Convert.ToBase64String(signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)) + "\n",
                new UTF8Encoding(false));
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or FormatException or CryptographicException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void BuildApplicationRelease(
        string version,
        string stagingDirectory,
        string outputDirectory,
        DateTimeOffset publishedAt,
        Key signingKey)
    {
        var assetName = $"KeyRadar-v{version}-windows-x64.zip";
        var assetPath = Path.Combine(outputDirectory, assetName);
        CreateDeterministicZip(stagingDirectory, assetPath);
        var hash = SHA256.HashData(File.ReadAllBytes(assetPath));
        WriteHashFile(assetPath, hash);

        var manifest = new
        {
            schemaVersion = 1,
            version,
            assetName,
            downloadUrl = $"https://github.com/LizzardKevin/KeyRadar/releases/download/v{version}/{assetName}",
            sha256 = Convert.ToHexString(hash).ToLowerInvariant(),
            publishedAtUtc = publishedAt.ToUniversalTime(),
        };
        WriteSignedJson("keyradar-latest", manifest, outputDirectory, signingKey);
    }

    private static string BuildRuleRelease(
        string version,
        string rulesDirectory,
        string outputDirectory,
        DateTimeOffset publishedAt,
        Key signingKey)
    {
        var ruleDocuments = Directory.EnumerateFiles(rulesDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Select(path => new RuleDocument(
                Path.GetFileName(path),
                File.ReadAllBytes(path)))
            .ToArray();
        if (ruleDocuments.Length == 0)
        {
            throw new InvalidDataException("The official source must contain at least one rule document.");
        }

        var packManifest = new
        {
            schemaVersion = 2,
            packId = OfficialRulePack.PackId,
            version,
            files = ruleDocuments.Select(rule => new
            {
                path = $"rules/{rule.FileName}",
                sha256 = Convert.ToHexString(SHA256.HashData(rule.Content)).ToLowerInvariant(),
            }),
        };
        var packManifestBytes = JsonSerializer.SerializeToUtf8Bytes(packManifest, JsonOptions);
        var packSignature = SignatureAlgorithm.Ed25519.Sign(signingKey, packManifestBytes);
        var assetName = $"KeyRadar-Rules-v{version}.krpack";
        var assetPath = Path.Combine(outputDirectory, assetName);
        CreateRulePack(assetPath, packManifestBytes, packSignature, ruleDocuments);
        using (var packageStream = File.OpenRead(assetPath))
        {
            var validation = RulePackReader.Read(
                packageStream,
                signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            if (!validation.IsSuccess || validation.Pack!.Variants.Count != ruleDocuments.Length)
            {
                throw new InvalidDataException($"Generated rule pack is invalid: {validation.Message}");
            }
        }

        var hash = SHA256.HashData(File.ReadAllBytes(assetPath));
        WriteHashFile(assetPath, hash);

        var latestManifest = new
        {
            schemaVersion = 1,
            packId = OfficialRulePack.PackId,
            version,
            assetName,
            downloadUrl = $"https://github.com/LizzardKevin/KeyRadar/releases/download/v{version}/{assetName}",
            sha256 = Convert.ToHexString(hash).ToLowerInvariant(),
            publishedAtUtc = publishedAt.ToUniversalTime(),
        };
        WriteSignedJson("keyradar-rules-latest", latestManifest, outputDirectory, signingKey);
        return assetPath;
    }

    private static void CreateDeterministicZip(string sourceDirectory, string outputPath)
    {
        using var output = File.Create(outputPath);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            WriteZipEntry(archive, relativePath, File.ReadAllBytes(file));
        }
    }

    private static void CreateRulePack(
        string outputPath,
        byte[] manifestBytes,
        byte[] signature,
        IReadOnlyList<RuleDocument> rules)
    {
        using var output = File.Create(outputPath);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
        WriteZipEntry(archive, "manifest.json", manifestBytes);
        WriteZipEntry(archive, "signature.ed25519", Encoding.ASCII.GetBytes(Convert.ToBase64String(signature)));
        foreach (var rule in rules)
        {
            WriteZipEntry(archive, $"rules/{rule.FileName}", rule.Content);
        }
    }

    private static void WriteZipEntry(ZipArchive archive, string name, ReadOnlySpan<byte> content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicTimestamp;
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static void WriteSignedJson(string baseName, object value, string outputDirectory, Key signingKey)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        File.WriteAllBytes(Path.Combine(outputDirectory, $"{baseName}.json"), bytes);
        var signature = SignatureAlgorithm.Ed25519.Sign(signingKey, bytes);
        File.WriteAllText(
            Path.Combine(outputDirectory, $"{baseName}.json.sig"),
            Convert.ToBase64String(signature) + "\n",
            new UTF8Encoding(false));
    }

    private static void WriteHashFile(string assetPath, ReadOnlySpan<byte> hash)
    {
        File.WriteAllText(
            assetPath + ".sha256",
            $"{Convert.ToHexString(hash).ToLowerInvariant()}  {Path.GetFileName(assetPath)}\n",
            new UTF8Encoding(false));
    }

    private static Key ImportSigningKey(string privateKeyText)
    {
        var privateKey = Convert.FromBase64String(privateKeyText.Trim());
        return Key.Import(
            SignatureAlgorithm.Ed25519,
            privateKey,
            KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.None });
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Arguments must be supplied as --name value pairs.");
            }

            result[args[index][2..]] = args[index + 1];
        }

        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument --{name}.");

    private static string ExistingDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        return fullPath;
    }

    private sealed record RuleDocument(string FileName, byte[] Content);
}
