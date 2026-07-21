using System.Globalization;
using System.Text.Json;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Configuration;

/// <summary>
/// Reads only bounded, rule-declared configuration fields.  No application name,
/// path, field name, or function mapping is baked into this reader.
/// </summary>
public sealed class DeclarativeApplicationConfigurationReader(
    Func<ConfigurationSourceRoot, string>? rootResolver = null) : IApplicationConfigurationReader
{
    private const int MaximumEntriesPerSource = 128;
    private readonly Func<ConfigurationSourceRoot, string> _rootResolver = rootResolver ?? ResolveRoot;

    public bool Supports(ApplicationVariantRule variant) =>
        variant.IsConfigurationReadAuthorized && variant.ConfigurationSources.Count > 0;

    public async Task<IReadOnlyList<LocalConfigurationHotkey>> ReadAsync(
        ProcessDescriptor process,
        ApplicationVariantRule variant,
        CancellationToken cancellationToken)
    {
        if (!Supports(variant)) return [];

        var results = new List<LocalConfigurationHotkey>();
        foreach (var source in variant.ConfigurationSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var path = ResolveSafePath(source);
                if (path is null || !IsSafeRegularFile(path, source.MaxBytes)) continue;
                var discovered = source.Format switch
                {
                    ConfigurationSourceFormat.Json => await ReadJsonAsync(path, variant, source, cancellationToken).ConfigureAwait(false),
                    ConfigurationSourceFormat.Ini => await ReadIniAsync(path, variant, source, cancellationToken).ConfigureAwait(false),
                    _ => [],
                };
                results.AddRange(discovered);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                // A changed, locked, or malformed application configuration is not fatal to a scan.
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<LocalConfigurationHotkey>> ReadJsonAsync(
        string path,
        ApplicationVariantRule variant,
        ConfigurationSourceRule source,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var results = new List<LocalConfigurationHotkey>();
        foreach (var entry in source.Entries)
        {
            var candidates = entry.CollectionSelector is null
                ? [document.RootElement]
                : Select(document.RootElement, entry.CollectionSelector).ValueKind == JsonValueKind.Array
                    ? Select(document.RootElement, entry.CollectionSelector).EnumerateArray().Take(MaximumEntriesPerSource).ToArray()
                    : [];
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryGet(candidate, entry.GestureSelector, out var encoded) ||
                    !GestureDecoders.TryDecode(entry.Decoder, encoded,
                        entry.WinSelector is null || !TryGet(candidate, entry.WinSelector, out var win) ? default : win,
                        out var gesture)) continue;
                results.Add(CreateHotkey(variant, source, entry, candidate, gesture));
            }
        }
        return results;
    }

    private async Task<IReadOnlyList<LocalConfigurationHotkey>> ReadIniAsync(
        string path,
        ApplicationVariantRule variant,
        ConfigurationSourceRule source,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            if (source.Entries.Any(entry => entry.GestureSelector.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                values[key] = line[(separator + 1)..].Trim();
            }
        }

        var results = new List<LocalConfigurationHotkey>();
        foreach (var entry in source.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.CollectionSelector is not null || !values.TryGetValue(entry.GestureSelector, out var value) ||
                !GestureDecoders.TryDecode(entry.Decoder, value, default, out var gesture)) continue;
            results.Add(CreateHotkey(variant, source, entry, default, gesture));
        }
        return results;
    }

    private static LocalConfigurationHotkey CreateHotkey(
        ApplicationVariantRule variant, ConfigurationSourceRule source, ConfigurationEntryRule entry,
        JsonElement candidate, HotkeyGesture gesture)
    {
        var mappingValue = entry.FunctionSelector is not null && TryGet(candidate, entry.FunctionSelector, out var value)
            ? Scalar(value) : null;
        var function = mappingValue is not null && entry.FunctionValues.TryGetValue(mappingValue, out var mapped)
            ? mapped : entry.Function;
        var commandId = mappingValue is not null && entry.CommandIdValues.TryGetValue(mappingValue, out var mappedCommandId)
            ? mappedCommandId : entry.CommandId;
        return new LocalConfigurationHotkey(
            variant.ApplicationId, gesture,
            function.Resolve(CultureInfo.CurrentUICulture.Name), entry.Scope,
            $"{source.Format} configuration source {source.SourceId}", CommandId: commandId);
    }

    private string? ResolveSafePath(ConfigurationSourceRule source)
    {
        var root = Path.GetFullPath(_rootResolver(source.Root));
        var candidate = Path.GetFullPath(Path.Combine(root, source.RelativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }

    private static bool IsSafeRegularFile(string path, int maxBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is 0 || info.Length > maxBytes) return false;
        for (var directory = info.Directory; directory is not null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        }
        return (info.Attributes & FileAttributes.ReparsePoint) == 0;
    }

    private static JsonElement Select(JsonElement element, string selector) =>
        TryGet(element, selector, out var result) ? result : default;

    private static bool TryGet(JsonElement element, string selector, out JsonElement result)
    {
        result = element;
        foreach (var segment in selector.Split('.'))
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(segment, out result)) return false;
        }
        return true;
    }

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static string ResolveRoot(ConfigurationSourceRoot root) => root switch
    {
        ConfigurationSourceRoot.LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ConfigurationSourceRoot.RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ConfigurationSourceRoot.Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ConfigurationSourceRoot.ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        _ => throw new ArgumentOutOfRangeException(nameof(root)),
    };
}

internal static class GestureDecoders
{
    public static bool TryDecode(ConfigurationGestureDecoder decoder, JsonElement value, JsonElement win, out HotkeyGesture gesture) =>
        decoder switch
        {
            ConfigurationGestureDecoder.GestureString => TryDecodeGestureString(value.GetString(), out gesture),
            ConfigurationGestureDecoder.VirtualKeyArray => TryDecodeVirtualKeyArray(value, out gesture),
            ConfigurationGestureDecoder.WinFormsHotkey => TryDecodeWinForms(value, win.ValueKind == JsonValueKind.True, out gesture),
            _ => Fail(out gesture),
        };

    public static bool TryDecode(ConfigurationGestureDecoder decoder, string value, JsonElement win, out HotkeyGesture gesture) =>
        decoder == ConfigurationGestureDecoder.GestureString ? TryDecodeGestureString(value, out gesture) : Fail(out gesture);

    private static bool TryDecodeGestureString(string? value, out HotkeyGesture gesture)
    {
        gesture = default;
        return !string.IsNullOrWhiteSpace(value) &&
            !value.Equals("None", StringComparison.OrdinalIgnoreCase) &&
            HotkeyGesture.TryParse(value.Replace(" ", string.Empty, StringComparison.Ordinal), out gesture);
    }

    private static bool TryDecodeVirtualKeyArray(JsonElement value, out HotkeyGesture gesture)
    {
        gesture = default;
        if (value.ValueKind != JsonValueKind.Array) return false;
        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var item in value.EnumerateArray())
        {
            if (!item.TryGetInt32(out var vk)) return false;
            modifiers |= vk switch
            {
                0x10 => HotkeyModifiers.Shift, 0x11 => HotkeyModifiers.Control, 0x12 => HotkeyModifiers.Alt,
                0x5B or 0x5C => HotkeyModifiers.Windows, _ => HotkeyModifiers.None,
            };
            if (vk is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C))
            {
                if (key is not null || !VirtualKeyNames.TryGet(vk, out key)) return false;
            }
        }
        if (key is null) return false;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    private static bool TryDecodeWinForms(JsonElement value, bool win, out HotkeyGesture gesture)
    {
        gesture = default;
        var text = value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.GetString();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var encoded) ||
            !VirtualKeyNames.TryGet(encoded & 0xffff, out var key)) return false;
        var modifiers = HotkeyModifiers.None;
        if ((encoded & 0x20000) != 0) modifiers |= HotkeyModifiers.Control;
        if ((encoded & 0x10000) != 0) modifiers |= HotkeyModifiers.Shift;
        if ((encoded & 0x40000) != 0) modifiers |= HotkeyModifiers.Alt;
        if (win) modifiers |= HotkeyModifiers.Windows;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    private static bool Fail(out HotkeyGesture gesture) { gesture = default; return false; }
}

internal static class VirtualKeyNames
{
    public static bool TryGet(int value, out string key)
    {
        if (value is >= 0x41 and <= 0x5A) { key = ((char)value).ToString(); return true; }
        if (value is >= 0x30 and <= 0x39) { key = ((char)value).ToString(); return true; }
        if (value is >= 0x70 and <= 0x87) { key = $"F{value - 0x6F}"; return true; }
        key = value switch
        {
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x1B => "Esc", 0x20 => "Space",
            0x21 => "PageUp", 0x22 => "PageDown", 0x23 => "End", 0x24 => "Home", 0x25 => "Left",
            0x26 => "Up", 0x27 => "Right", 0x28 => "Down", 0x2C => "PrintScreen", 0x2D => "Insert", 0x2E => "Delete",
            0xAD => "VolumeMute", 0xAE => "VolumeDown", 0xAF => "VolumeUp", _ => string.Empty,
        };
        return key.Length > 0;
    }
}
