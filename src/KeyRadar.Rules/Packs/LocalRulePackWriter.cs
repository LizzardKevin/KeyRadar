using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using KeyRadar.Conflicts;

namespace KeyRadar.Rules.Packs;

public static class LocalRulePackWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static void Write(Stream destination, IReadOnlyList<ApplicationVariantRule> variants)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(variants);
        if (variants.Count == 0)
        {
            throw new ArgumentException("A local rule pack must contain at least one variant.", nameof(variants));
        }

        var documents = variants.Select(variant => new Document(
            $"rules/{variant.ApplicationId}-{variant.VariantId}.json",
            JsonSerializer.SerializeToUtf8Bytes(ToDocument(variant), JsonOptions))).ToArray();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            packId = "keyradar.local",
            version = "1.0.0",
            source = "local",
            files = documents.Select(document => new
            {
                path = document.Path,
                sha256 = Convert.ToHexString(SHA256.HashData(document.Content)).ToLowerInvariant(),
            }),
        }, JsonOptions);

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(archive, "manifest.json", manifest);
        foreach (var document in documents)
        {
            WriteEntry(archive, document.Path, document.Content);
        }
    }

    public static void SaveAtomically(string path, IReadOnlyList<ApplicationVariantRule> variants)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                Write(stream, variants);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static object ToDocument(ApplicationVariantRule variant) => new
    {
        schemaVersion = 2,
        applicationId = variant.ApplicationId,
        variantId = variant.VariantId,
        displayName = variant.DisplayName.Values,
        match = new
        {
            executables = variant.Match.Executables,
            publishers = variant.Match.Publishers,
            versionRange = variant.Match.VersionRange?.Expression,
            packageFamilyNames = variant.Match.PackageFamilyNames,
            distribution = variant.Match.Distribution,
        },
        hotkeys = variant.Hotkeys.Select(hotkey => new
        {
            gesture = hotkey.Gesture.ToString(),
            function = hotkey.Function.Values,
            scope = hotkey.Scope switch
            {
                HotkeyScope.Foreground => "foreground",
                HotkeyScope.Background => "background",
                HotkeyScope.Global => "global",
                HotkeyScope.WindowsSystem => "windowsSystem",
                _ => throw new InvalidDataException("Unknown hotkey scope."),
            },
            confidence = "userDeclared",
            sources = hotkey.Sources,
            commandId = hotkey.CommandId,
        }),
        // Preserve user input for round-trip transparency; RulePackReader deliberately
        // discards these declarations for unsigned local packs.
        configurationSources = variant.ConfigurationSources.Select(source => new
        {
            sourceId = source.SourceId,
            root = source.Root switch
            {
                ConfigurationSourceRoot.LocalAppData => "localAppData",
                ConfigurationSourceRoot.RoamingAppData => "roamingAppData",
                ConfigurationSourceRoot.Documents => "documents",
                ConfigurationSourceRoot.ProgramData => "programData",
                _ => throw new InvalidDataException("Unknown configuration root."),
            },
            relativePath = source.RelativePath,
            format = source.Format == ConfigurationSourceFormat.Json ? "json" : "ini",
            maxBytes = source.MaxBytes,
            entries = source.Entries.Select(entry => new
            {
                commandId = entry.CommandId,
                gestureSelector = entry.GestureSelector,
                collectionSelector = entry.CollectionSelector,
                winSelector = entry.WinSelector,
                functionSelector = entry.FunctionSelector,
                decoder = entry.Decoder switch
                {
                    ConfigurationGestureDecoder.GestureString => "gestureString",
                    ConfigurationGestureDecoder.VirtualKeyArray => "virtualKeyArray",
                    ConfigurationGestureDecoder.WinFormsHotkey => "winFormsHotkey",
                    _ => throw new InvalidDataException("Unknown configuration decoder."),
                },
                function = entry.Function.Values,
                functionValues = entry.FunctionValues.ToDictionary(pair => pair.Key, pair => pair.Value.Values),
                commandIdValues = entry.CommandIdValues,
                scope = entry.Scope switch
                {
                    HotkeyScope.Foreground => "foreground", HotkeyScope.Background => "background",
                    HotkeyScope.Global => "global", HotkeyScope.WindowsSystem => "windowsSystem",
                    _ => throw new InvalidDataException("Unknown hotkey scope."),
                },
            }),
        }),
    };

    private static void WriteEntry(ZipArchive archive, string path, ReadOnlySpan<byte> content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private sealed record Document(string Path, byte[] Content);
}
