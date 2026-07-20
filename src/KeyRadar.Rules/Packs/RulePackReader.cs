using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;

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
        => ReadInternal(packageStream, publicKeyBytes, isLocal: false);

    public static RulePackReadResult ReadLocal(Stream packageStream) =>
        ReadInternal(packageStream, [], isLocal: true);

    private static RulePackReadResult ReadInternal(
        Stream packageStream,
        ReadOnlySpan<byte> publicKeyBytes,
        bool isLocal)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        try
        {
            using var bufferedPackage = Buffer(packageStream);
            var validation = isLocal
                ? RulePackValidator.ValidateLocal(bufferedPackage)
                : RulePackValidator.Validate(bufferedPackage, publicKeyBytes);
            if (!validation.IsValid)
            {
                return RulePackReadResult.Failure(RulePackReadError.ValidationFailed, validation.Message);
            }

            bufferedPackage.Position = 0;
            using var archive = new ZipArchive(bufferedPackage, ZipArchiveMode.Read, leaveOpen: true);
            var manifest = JsonSerializer.Deserialize<RulePackManifest>(
                ReadEntry(archive.GetEntry("manifest.json")!),
                JsonOptions)!;
            if (isLocal != (manifest.PackId == "keyradar.local" && manifest.Source == "local"))
            {
                return RulePackReadResult.Failure(
                    RulePackReadError.ValidationFailed,
                    "The rule-pack identity does not match its trust level.");
            }
            var variants = new List<ApplicationVariantRule>(manifest.Files.Count);
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in manifest.Files)
            {
                var document = JsonSerializer.Deserialize<RuleDocument>(
                    ReadEntry(archive.GetEntry(file.Path)!),
                    JsonOptions);
                if (!TryCreateVariant(file.Path, document, out var variant, out var error))
                {
                    return RulePackReadResult.Failure(RulePackReadError.InvalidRule, error);
                }

                var identity = $"{variant!.ApplicationId}/{variant.VariantId}";
                if (!identities.Add(identity))
                {
                    return RulePackReadResult.Failure(
                        RulePackReadError.DuplicateApplicationVariant,
                        $"The pack declares application variant '{identity}' more than once.");
                }

                variants.Add(variant);
            }

            return RulePackReadResult.Success(new RulePack(
                manifest.PackId,
                manifest.Version,
                variants,
                isLocal ? RulePackTrust.UnsignedLocal : RulePackTrust.SignedOfficial));
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

    private static bool TryCreateVariant(
        string path,
        RuleDocument? document,
        out ApplicationVariantRule? variant,
        out string error)
    {
        variant = null;
        error = $"The rule '{path}' does not conform to schema version 2.";
        if (document is null ||
            document.SchemaVersion != 2 ||
            !IsIdentifier(document.ApplicationId, 100) ||
            !IsIdentifier(document.VariantId, 100) ||
            document.DisplayName is not { Count: > 0 and <= 8 } ||
            document.Match is null ||
            document.Hotkeys is not { Count: <= 512 })
        {
            return false;
        }

        LocalizedText displayName;
        try
        {
            displayName = new LocalizedText(document.DisplayName);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var isWindowsSystem = document.ApplicationId.Equals("windows-system", StringComparison.Ordinal);
        if (!TryCreateMatch(document.Match, isWindowsSystem, out var match))
        {
            return false;
        }

        var hotkeys = new List<HotkeyRule>(document.Hotkeys.Count);
        foreach (var item in document.Hotkeys)
        {
            if (!TryCreateHotkey(item, isWindowsSystem, out var hotkey))
            {
                return false;
            }

            hotkeys.Add(hotkey!);
        }

        variant = new ApplicationVariantRule(
            document.ApplicationId,
            document.VariantId,
            displayName,
            match!,
            hotkeys);
        return true;
    }

    private static bool TryCreateMatch(
        RuleMatch document,
        bool isWindowsSystem,
        out ApplicationMatchRule? match)
    {
        match = null;
        if (document.Executables is not { Count: <= 32 } ||
            document.Publishers is not { Count: <= 16 } ||
            document.PackageFamilyNames is not { Count: <= 16 } ||
            isWindowsSystem && document.Executables.Count != 0 ||
            !isWindowsSystem && document.Executables.Count == 0 ||
            document.Executables.Any(executable => !IsExecutableName(executable)) ||
            HasDuplicates(document.Executables) ||
            document.Publishers.Any(publisher => !IsText(publisher, 200)) ||
            HasDuplicates(document.Publishers) ||
            document.PackageFamilyNames.Any(package => !IsPackageFamilyName(package)) ||
            HasDuplicates(document.PackageFamilyNames) ||
            document.Distribution is not null && !IsIdentifier(document.Distribution, 64))
        {
            return false;
        }

        VersionRange? versionRange = null;
        if (document.VersionRange is not null &&
            !VersionRange.TryParse(document.VersionRange, out versionRange))
        {
            return false;
        }

        match = new ApplicationMatchRule(
            document.Executables.ToArray(),
            document.Publishers.ToArray(),
            versionRange,
            document.PackageFamilyNames.ToArray(),
            document.Distribution);
        return true;
    }

    private static bool TryCreateHotkey(
        RuleHotkey? document,
        bool isWindowsSystem,
        out HotkeyRule? hotkey)
    {
        hotkey = null;
        if (document is null ||
            !IsText(document.Gesture, 64) ||
            !HotkeyGesture.TryParse(document.Gesture, out var gesture) ||
            document.Function is not { Count: > 0 and <= 8 } ||
            !TryParseScope(document.Scope, out var scope) ||
            isWindowsSystem != (scope == HotkeyScope.WindowsSystem) ||
            !TryParseConfidence(document.Confidence, out var confidence) ||
            document.Sources is { Count: > 8 } ||
            document.Sources is not null && document.Sources.Any(source => !IsWebSource(source)))
        {
            return false;
        }

        LocalizedText function;
        try
        {
            function = new LocalizedText(document.Function);
        }
        catch (ArgumentException)
        {
            return false;
        }

        hotkey = new HotkeyRule(gesture, function, scope, confidence)
        {
            Sources = document.Sources?.ToArray() ?? [],
        };
        return true;
    }

    private static bool TryParseScope(string? value, out HotkeyScope scope)
    {
        scope = value switch
        {
            "foreground" => HotkeyScope.Foreground,
            "background" => HotkeyScope.Background,
            "global" => HotkeyScope.Global,
            "windowsSystem" => HotkeyScope.WindowsSystem,
            _ => (HotkeyScope)(-1),
        };
        return (int)scope >= 0;
    }

    private static bool TryParseConfidence(string? value, out OwnershipConfidence confidence)
    {
        confidence = value switch
        {
            "configuration" => OwnershipConfidence.LocalConfiguration,
            "userDeclared" => OwnershipConfidence.UserDeclared,
            "hardwareMapping" => OwnershipConfidence.HardwareMapping,
            "systemKnown" => OwnershipConfidence.SystemKnown,
            "officialDefault" => OwnershipConfidence.OfficialDefault,
            "corroborated" => OwnershipConfidence.Corroborated,
            "suspected" => OwnershipConfidence.Suspected,
            _ => (OwnershipConfidence)(-1),
        };
        return (int)confidence >= 0;
    }

    private static bool IsIdentifier(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length > maximumLength ||
            value[0] is '-' ||
            value[^1] is '-')
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

    private static bool IsPackageFamilyName(string? value) =>
        IsText(value, 255) &&
        value!.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0;

    private static bool IsText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Any(char.IsControl);

    private static bool IsWebSource(string? value) =>
        IsText(value, 500) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http";

    private static bool HasDuplicates(IEnumerable<string> values) =>
        values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count();

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
        if (entry.Length > MaximumRuleBytes &&
            !entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
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
        [property: JsonPropertyName("variantId")] string VariantId,
        [property: JsonPropertyName("displayName")] IReadOnlyDictionary<string, string> DisplayName,
        [property: JsonPropertyName("match")] RuleMatch Match,
        [property: JsonPropertyName("hotkeys")] IReadOnlyList<RuleHotkey?> Hotkeys);

    private sealed record RuleMatch(
        [property: JsonPropertyName("executables")] IReadOnlyList<string> Executables,
        [property: JsonPropertyName("publishers")] IReadOnlyList<string> Publishers,
        [property: JsonPropertyName("versionRange")] string? VersionRange,
        [property: JsonPropertyName("packageFamilyNames")] IReadOnlyList<string> PackageFamilyNames,
        [property: JsonPropertyName("distribution")] string? Distribution);

    private sealed record RuleHotkey(
        [property: JsonPropertyName("gesture")] string Gesture,
        [property: JsonPropertyName("function")] IReadOnlyDictionary<string, string> Function,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("confidence")] string Confidence,
        [property: JsonPropertyName("sources")] IReadOnlyList<string>? Sources = null);
}
