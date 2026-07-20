using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyRadar.Conflicts;
using KeyRadar.Shortcuts;

namespace KeyRadar.Rules.Packs;

public static class RulePackReader
{
    private const long MaximumArchiveBytes = 16 * 1024 * 1024;
    private const int MaximumRuleBytes = 512 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static RulePackReadResult Read(Stream packageStream, ReadOnlySpan<byte> publicKeyBytes)
        => ReadCore(packageStream, publicKeyBytes, isUnsignedLocal: false);

    public static RulePackReadResult ReadUnsignedLocal(Stream packageStream)
        => ReadCore(packageStream, [], isUnsignedLocal: true);

    private static RulePackReadResult ReadCore(
        Stream packageStream,
        ReadOnlySpan<byte> publicKeyBytes,
        bool isUnsignedLocal)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        try
        {
            using var bufferedPackage = Buffer(packageStream);
            var validation = isUnsignedLocal
                ? RulePackValidator.ValidateUnsignedLocal(bufferedPackage)
                : RulePackValidator.Validate(bufferedPackage, publicKeyBytes);
            if (!validation.IsValid)
            {
                return RulePackReadResult.Failure(RulePackReadError.ValidationFailed, validation.Message);
            }

            bufferedPackage.Position = 0;
            using var archive = new ZipArchive(bufferedPackage, ZipArchiveMode.Read, leaveOpen: true);
            var manifestEntry = archive.GetEntry("manifest.json")!;
            var manifest = JsonSerializer.Deserialize<RulePackManifest>(ReadEntry(manifestEntry), JsonOptions)!;
            var applications = new List<ApplicationRuleSet>(manifest.Files.Count);
            var applicationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in manifest.Files)
            {
                var document = JsonSerializer.Deserialize<RuleDocument>(
                    ReadEntry(archive.GetEntry(file.Path)!),
                    JsonOptions);
                if (document is not null && applicationIds.Contains(document.ApplicationId))
                {
                    return RulePackReadResult.Failure(
                        RulePackReadError.DuplicateApplication,
                        $"The pack declares application '{document.ApplicationId}' more than once.");
                }

                if (!TryCreateRuleSet(file.Path, document, isUnsignedLocal, out var rules, out var error))
                {
                    return RulePackReadResult.Failure(RulePackReadError.InvalidRule, error);
                }

                if (!applicationIds.Add(rules!.Id))
                {
                    return RulePackReadResult.Failure(
                        RulePackReadError.DuplicateApplication,
                        $"The pack declares application '{rules.Id}' more than once.");
                }

                applications.Add(rules);
            }

            return RulePackReadResult.Success(new RulePack(manifest.PackId, manifest.Version, applications));
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            JsonException or
            FormatException or
            ArgumentException)
        {
            return RulePackReadResult.Failure(
                RulePackReadError.InvalidRule,
                "A rule document is malformed or exceeds its declared limits.");
        }
    }

    private static bool TryCreateRuleSet(
        string path,
        RuleDocument? document,
        bool isUnsignedLocal,
        out ApplicationRuleSet? rules,
        out string error)
    {
        rules = null;
        error = $"The rule '{path}' does not conform to schema version 1.";
        if (document is null ||
            document.SchemaVersion != 1 ||
            !IsValidApplicationId(document.ApplicationId) ||
            !path.Equals($"rules/{document.ApplicationId}.json", StringComparison.OrdinalIgnoreCase) ||
            !IsText(document.DisplayName, 100) ||
            document.Executables is not { Count: >= 1 and <= 32 } ||
            document.Executables.Any(executable => !IsExecutableName(executable)) ||
            document.Executables.Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Executables.Count ||
            document.Shortcuts is not { Count: <= 512 })
        {
            return false;
        }

        var shortcuts = new List<ShortcutRule>(document.Shortcuts.Count);
        foreach (var item in document.Shortcuts)
        {
            if (item is null ||
                !IsText(item.Gesture, 64) ||
                !ShortcutGesture.TryParse(item.Gesture, out var gesture) ||
                !IsText(item.Function, 160) ||
                !TryParseScope(item.Scope, out var scope) ||
                !TryParseConfidence(item.Confidence, out var confidence) ||
                item.Sources is { Count: > 8 } ||
                item.Sources is not null && item.Sources.Any(source => !IsWebSource(source)))
            {
                return false;
            }

            shortcuts.Add(new ShortcutRule(gesture, item.Function, scope, confidence)
            {
                Sources = item.Sources?.ToArray() ?? [],
                Origin = isUnsignedLocal ? RuleOrigin.LocalUnsigned : RuleOrigin.SignedRulePack,
            });
        }

        rules = new ApplicationRuleSet(
            document.ApplicationId,
            document.DisplayName,
            document.Executables.ToArray(),
            shortcuts);
        return true;
    }

    private static bool TryParseScope(string? value, out ShortcutScope scope)
    {
        scope = value switch
        {
            "application" => ShortcutScope.Application,
            "global" => ShortcutScope.Global,
            "windowsSystem" => ShortcutScope.WindowsSystem,
            _ => (ShortcutScope)(-1),
        };
        return (int)scope >= 0;
    }

    private static bool TryParseConfidence(string? value, out OwnershipConfidence confidence)
    {
        confidence = value switch
        {
            "configuration" => OwnershipConfidence.Configuration,
            "systemKnown" => OwnershipConfidence.SystemKnown,
            "suspected" => OwnershipConfidence.Suspected,
            _ => (OwnershipConfidence)(-1),
        };
        return (int)confidence >= 0;
    }

    private static bool IsValidApplicationId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 100 || value[0] is '-' || value[^1] is '-')
        {
            return false;
        }

        var previousWasDash = false;
        foreach (var character in value)
        {
            if (character is '-')
            {
                if (previousWasDash)
                {
                    return false;
                }

                previousWasDash = true;
                continue;
            }

            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9'))
            {
                return false;
            }

            previousWasDash = false;
        }

        return true;
    }

    private static bool IsExecutableName(string? value) =>
        IsText(value, 260) &&
        value!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
        value.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0;

    private static bool IsText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Any(char.IsControl);

    private static bool IsWebSource(string? value) =>
        IsText(value, 500) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http";

    private static MemoryStream Buffer(Stream source)
    {
        var destination = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (destination.Length + read > MaximumArchiveBytes)
            {
                throw new InvalidDataException("The compressed rule pack exceeds 16 MiB.");
            }

            destination.Write(chunk, 0, read);
        }

        destination.Position = 0;
        return destination;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > MaximumRuleBytes && !entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The rule entry exceeds 512 KiB.");
        }

        using var source = entry.Open();
        using var destination = new MemoryStream((int)entry.Length);
        source.CopyTo(destination);
        return destination.ToArray();
    }

    private sealed record RuleDocument(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("applicationId")] string ApplicationId,
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("executables")] IReadOnlyList<string> Executables,
        [property: JsonPropertyName("shortcuts")] IReadOnlyList<RuleShortcut?> Shortcuts);

    private sealed record RuleShortcut(
        [property: JsonPropertyName("gesture")] string Gesture,
        [property: JsonPropertyName("function")] string Function,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("confidence")] string Confidence,
        [property: JsonPropertyName("sources")] IReadOnlyList<string>? Sources = null);
}
