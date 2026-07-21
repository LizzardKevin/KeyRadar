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

    /// <summary>Used only by the Debug-only pack built from this repository.</summary>
    public static RulePackReadResult ReadDevelopment(Stream packageStream) =>
        ReadInternal(packageStream, [], isLocal: true, authorizeConfigurationSources: true);

    private static RulePackReadResult ReadInternal(
        Stream packageStream,
        ReadOnlySpan<byte> publicKeyBytes,
        bool isLocal,
        bool authorizeConfigurationSources = false)
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
                if (!TryCreateVariant(file.Path, document, !isLocal || authorizeConfigurationSources, out var variant, out var error))
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
        bool authorizeConfigurationSources,
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

        if (hotkeys.Where(hotkey => hotkey.CommandId is not null)
            .GroupBy(hotkey => hotkey.CommandId!, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            return false;
        }

        if (!TryCreateConfigurationSources(document.ConfigurationSources, authorizeConfigurationSources, out var configurationSources))
        {
            return false;
        }

        variant = new ApplicationVariantRule(
            document.ApplicationId,
            document.VariantId,
            displayName,
            match!,
            hotkeys)
        {
            ConfigurationSources = configurationSources!,
            IsConfigurationReadAuthorized = authorizeConfigurationSources && configurationSources!.Count > 0,
        };
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
            CommandId = document.CommandId is null ? null : IsIdentifier(document.CommandId, 100) ? document.CommandId : null,
        };
        return document.CommandId is null || hotkey.CommandId is not null;
    }

    private static bool TryCreateConfigurationSources(
        IReadOnlyList<RuleConfigurationSource?>? documents,
        bool authorized,
        out IReadOnlyList<ConfigurationSourceRule>? sources)
    {
        sources = [];
        if (documents is null) return true;
        // User-declared packs retain their shortcuts but never receive file/registry read authority.
        if (!authorized) return true;
        if (documents.Count > 8) return false;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ConfigurationSourceRule>(documents.Count);
        foreach (var source in documents)
        {
            if (source is null || !IsIdentifier(source.SourceId, 100) || !ids.Add(source.SourceId) ||
                !TryParseRoot(source.Root, out var root) || !TryParseFormat(source.Format, out var format) ||
                !IsSafeRelativePath(source.RelativePath) || source.MaxBytes is <= 0 or > 8 * 1024 * 1024 ||
                source.Entries is not { Count: > 0 and <= 128 }) return false;
            var entries = new List<ConfigurationEntryRule>(source.Entries.Count);
            var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in source.Entries)
            {
                if (entry is null || !IsIdentifier(entry.CommandId, 100) || !commands.Add(entry.CommandId) ||
                    !IsSelector(entry.GestureSelector) || entry.CollectionSelector is not null && !IsSelector(entry.CollectionSelector) ||
                    entry.WinSelector is not null && !IsSelector(entry.WinSelector) || entry.FunctionSelector is not null && !IsSelector(entry.FunctionSelector) ||
                    !TryParseDecoder(entry.Decoder, out var decoder) || !TryParseScope(entry.Scope, out var scope) ||
                    !TryLocalized(entry.Function, out var function) ||
                    !TryCreateTextMap(entry.FunctionValues, out var functionValues) ||
                    !TryCreateCommandMap(entry.CommandIdValues, out var commandValues)) return false;
                if (format == ConfigurationSourceFormat.Ini && (entry.CollectionSelector is not null || decoder != ConfigurationGestureDecoder.GestureString || entry.WinSelector is not null)) return false;
                entries.Add(new ConfigurationEntryRule(entry.CommandId, entry.GestureSelector, decoder, function!, scope)
                {
                    CollectionSelector = entry.CollectionSelector,
                    WinSelector = entry.WinSelector,
                    FunctionSelector = entry.FunctionSelector,
                    FunctionValues = functionValues!,
                    CommandIdValues = commandValues!,
                });
            }
            result.Add(new ConfigurationSourceRule(source.SourceId, root, source.RelativePath, format, source.MaxBytes, entries));
        }
        if (result.SelectMany(source => source.Entries)
            .SelectMany(entry => new[] { entry.CommandId }.Concat(entry.CommandIdValues.Values).Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(command => command, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1)) return false;
        sources = result;
        return true;
    }

    private static bool TryLocalized(IReadOnlyDictionary<string, string>? value, out LocalizedText? text)
    {
        text = null;
        if (value is not { Count: > 0 and <= 8 }) return false;
        try { text = new LocalizedText(value); return true; } catch (ArgumentException) { return false; }
    }

    private static bool TryCreateTextMap(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? values, out IReadOnlyDictionary<string, LocalizedText>? result)
    {
        result = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
        if (values is null) return true;
        if (values.Count > 64 || values.Any(pair => !IsMappingKey(pair.Key) || !TryLocalized(pair.Value, out _))) return false;
        result = values.ToDictionary(pair => pair.Key, pair => { TryLocalized(pair.Value, out var text); return text!; }, StringComparer.Ordinal);
        return true;
    }

    private static bool TryCreateCommandMap(IReadOnlyDictionary<string, string>? values, out IReadOnlyDictionary<string, string>? result)
    {
        result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values is null) return true;
        if (values.Count > 64 || values.Any(pair => !IsMappingKey(pair.Key) || !IsIdentifier(pair.Value, 100))) return false;
        result = new Dictionary<string, string>(values, StringComparer.Ordinal);
        return true;
    }

    private static bool IsMappingKey(string? value) => IsText(value, 64) && value!.IndexOfAny(['.', '/', '\\', ':', '*', '?']) < 0;
    private static bool IsSelector(string? value) => IsText(value, 200) && value!.Split('.').Length <= 8 && value.Split('.').All(segment => segment.Length is > 0 and <= 64 && segment.All(character => char.IsLetterOrDigit(character) || character is '_' or '-'));
    private static bool IsSafeRelativePath(string? value) => IsText(value, 260) && !Path.IsPathRooted(value) && !value!.Contains("..", StringComparison.Ordinal) && !value.Contains('%') && value.IndexOfAny(['*', '?', '"', '<', '>', '|', ':']) < 0;
    private static bool TryParseRoot(string? value, out ConfigurationSourceRoot root) => (root = value switch { "localAppData" => ConfigurationSourceRoot.LocalAppData, "roamingAppData" => ConfigurationSourceRoot.RoamingAppData, "documents" => ConfigurationSourceRoot.Documents, "programData" => ConfigurationSourceRoot.ProgramData, _ => (ConfigurationSourceRoot)(-1) }) >= 0;
    private static bool TryParseFormat(string? value, out ConfigurationSourceFormat format) => (format = value switch { "json" => ConfigurationSourceFormat.Json, "ini" => ConfigurationSourceFormat.Ini, _ => (ConfigurationSourceFormat)(-1) }) >= 0;
    private static bool TryParseDecoder(string? value, out ConfigurationGestureDecoder decoder) => (decoder = value switch { "gestureString" => ConfigurationGestureDecoder.GestureString, "virtualKeyArray" => ConfigurationGestureDecoder.VirtualKeyArray, "winFormsHotkey" => ConfigurationGestureDecoder.WinFormsHotkey, _ => (ConfigurationGestureDecoder)(-1) }) >= 0;

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
        [property: JsonPropertyName("hotkeys")] IReadOnlyList<RuleHotkey?> Hotkeys,
        [property: JsonPropertyName("configurationSources")] IReadOnlyList<RuleConfigurationSource?>? ConfigurationSources = null);

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
        [property: JsonPropertyName("sources")] IReadOnlyList<string>? Sources = null,
        [property: JsonPropertyName("commandId")] string? CommandId = null);

    private sealed record RuleConfigurationSource(
        [property: JsonPropertyName("sourceId")] string SourceId,
        [property: JsonPropertyName("root")] string Root,
        [property: JsonPropertyName("relativePath")] string RelativePath,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("maxBytes")] int MaxBytes,
        [property: JsonPropertyName("entries")] IReadOnlyList<RuleConfigurationEntry?> Entries);

    private sealed record RuleConfigurationEntry(
        [property: JsonPropertyName("commandId")] string CommandId,
        [property: JsonPropertyName("gestureSelector")] string GestureSelector,
        [property: JsonPropertyName("decoder")] string Decoder,
        [property: JsonPropertyName("function")] IReadOnlyDictionary<string, string> Function,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("collectionSelector")] string? CollectionSelector = null,
        [property: JsonPropertyName("winSelector")] string? WinSelector = null,
        [property: JsonPropertyName("functionSelector")] string? FunctionSelector = null,
        [property: JsonPropertyName("functionValues")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? FunctionValues = null,
        [property: JsonPropertyName("commandIdValues")] IReadOnlyDictionary<string, string>? CommandIdValues = null);
}
