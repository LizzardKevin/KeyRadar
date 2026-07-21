using System.Globalization;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Configuration;

/// <summary>
/// Reads only bounded, rule-declared configuration fields.  No application name,
/// path, field name, or function mapping is baked into this reader.
/// </summary>
public sealed class DeclarativeApplicationConfigurationReader(
    Func<ConfigurationSourceRoot, string>? rootResolver = null,
    IConfigurationSourceFileAccessor? fileAccessor = null) : IApplicationConfigurationReader
{
    private const int MaximumEntriesPerSource = 128;
    private readonly Func<ConfigurationSourceRoot, string> _rootResolver = rootResolver ?? ResolveRoot;
    private readonly IConfigurationSourceFileAccessor _fileAccessor = fileAccessor ?? new ConfigurationSourceFileAccessor();

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
                var root = Path.GetFullPath(_rootResolver(source.Root));
                var path = ResolveSafePath(root, source);
                if (path is null || HasReparsePoint(path, root)) continue;
                await using var stream = OpenValidatedSource(root, path);
                if (stream is null) continue;
                await using var snapshot = await ReadBoundedSnapshotAsync(stream, source.MaxBytes, cancellationToken)
                    .ConfigureAwait(false);
                if (snapshot is null) continue;
                var discovered = source.Format switch
                {
                    ConfigurationSourceFormat.Json => await ReadJsonAsync(snapshot, variant, source, cancellationToken).ConfigureAwait(false),
                    ConfigurationSourceFormat.Ini => await ReadIniAsync(snapshot, variant, source, cancellationToken).ConfigureAwait(false),
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
        Stream stream,
        ApplicationVariantRule variant,
        ConfigurationSourceRule source,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var results = new List<LocalConfigurationHotkey>();
        var remaining = MaximumEntriesPerSource;
        foreach (var entry in source.Entries)
        {
            if (remaining == 0) break;
            var collection = entry.CollectionSelector is null ? default : Select(document.RootElement, entry.CollectionSelector);
            IEnumerable<JsonElement> candidates = entry.CollectionSelector is null
                ? new[] { document.RootElement }
                : collection.ValueKind == JsonValueKind.Array ? collection.EnumerateArray() : [];
            foreach (var candidate in candidates)
            {
                if (remaining-- == 0) return results;
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
        Stream stream,
        ApplicationVariantRule variant,
        ConfigurationSourceRule source,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        var remaining = MaximumEntriesPerSource;
        foreach (var entry in source.Entries)
        {
            if (remaining-- == 0) break;
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

    private string? ResolveSafePath(string root, ConfigurationSourceRule source)
    {
        var candidate = Path.GetFullPath(Path.Combine(root, source.RelativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> bytes from the already validated handle and
    /// probes one additional byte. Parsers must never observe the mutable source stream directly.
    /// </summary>
    private static async Task<MemoryStream?> ReadBoundedSnapshotAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes <= 0) return null;

        var buffer = ArrayPool<byte>.Shared.Rent(maxBytes < 81920 ? maxBytes + 1 : 81920);
        try
        {
            var snapshot = new MemoryStream(Math.Min(maxBytes, 81920));
            var total = 0;
            while (total <= maxBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = maxBytes - total;
                var requested = remaining == 0 ? 1 : Math.Min(buffer.Length, remaining);
                var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    snapshot.Position = 0;
                    return snapshot;
                }

                if (read > maxBytes - total)
                {
                    snapshot.Dispose();
                    return null;
                }

                await snapshot.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
            }

            snapshot.Dispose();
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private Stream? OpenValidatedSource(string root, string path)
    {
        Stream? stream = null;
        try
        {
            stream = _fileAccessor.OpenRead(path);
            var finalPath = _fileAccessor.GetFinalPath(stream);
            if (!stream.CanRead ||
                !IsPathWithinRoot(finalPath, root) || !IsSameNormalizedPath(finalPath, path))
            {
                stream.Dispose();
                return null;
            }
            return stream;
        }
        catch
        {
            stream?.Dispose();
            throw;
        }
    }

    private static bool HasReparsePoint(string path, string root)
    {
        var current = new FileInfo(path);
        var rootPath = NormalizePath(root);
        for (var directory = current.Directory; directory is not null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            if (string.Equals(NormalizePath(directory.FullName), rootPath, StringComparison.OrdinalIgnoreCase)) break;
        }
        return (current.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    internal static bool IsPathWithinRoot(string? finalPath, string root) =>
        finalPath is not null &&
        NormalizePath(finalPath).StartsWith(NormalizePath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameNormalizedPath(string? first, string second) =>
        first is not null && string.Equals(NormalizePath(first), NormalizePath(second), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) normalized = @"\\" + normalized[8..];
        else if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)) normalized = normalized[4..];
        return Path.GetFullPath(normalized).TrimEnd(Path.DirectorySeparatorChar);
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
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var encoded))
        {
            return TryDecodeWinFormsKeysString(text, win, out gesture);
        }
        if (!VirtualKeyNames.TryGet(encoded & 0xffff, out var key)) return false;
        var modifiers = HotkeyModifiers.None;
        if ((encoded & 0x20000) != 0) modifiers |= HotkeyModifiers.Control;
        if ((encoded & 0x10000) != 0) modifiers |= HotkeyModifiers.Shift;
        if ((encoded & 0x40000) != 0) modifiers |= HotkeyModifiers.Alt;
        if (win) modifiers |= HotkeyModifiers.Windows;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    // System.Windows.Forms.KeysConverter serializes combinations as "R, Shift".
    // Keeping this in the generic WinForms decoder means configuration rules remain declarative.
    private static bool TryDecodeWinFormsKeysString(string? value, bool win, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase)) return false;

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 5) return false;
        for (var index = 0; index < parts.Length; index++)
        {
            parts[index] = parts[index] switch
            {
                "ControlKey" or "LControlKey" or "RControlKey" => "Control",
                "Menu" or "LMenu" or "RMenu" => "Alt",
                "ShiftKey" or "LShiftKey" or "RShiftKey" => "Shift",
                "LWin" or "RWin" => "Win",
                "Snapshot" => "PrintScreen",
                _ => parts[index],
            };
        }

        if (!HotkeyGesture.TryParse(string.Join('+', parts), out gesture)) return false;
        if (win) gesture = gesture with { Modifiers = gesture.Modifiers | HotkeyModifiers.Windows };
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
            0xAD => "VolumeMute", 0xAE => "VolumeDown", 0xAF => "VolumeUp",
            0xB0 => "MediaNextTrack", 0xB1 => "MediaPreviousTrack", 0xB2 => "MediaStop", 0xB3 => "MediaPlayPause",
            _ => string.Empty,
        };
        return key.Length > 0;
    }
}

/// <summary>Opens one configuration file and identifies the exact object that was opened.</summary>
public interface IConfigurationSourceFileAccessor
{
    Stream OpenRead(string path);
    string? GetFinalPath(Stream stream);
}

public sealed class ConfigurationSourceFileAccessor : IConfigurationSourceFileAccessor
{
    public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);

    public string? GetFinalPath(Stream stream)
    {
        if (stream is not FileStream fileStream) return null;
        if (!OperatingSystem.IsWindows()) return fileStream.Name;

        var capacity = 512;
        while (capacity <= 32768)
        {
            var buffer = new System.Text.StringBuilder(capacity);
            var length = GetFinalPathNameByHandle(fileStream.SafeFileHandle, buffer, buffer.Capacity, 0);
            if (length == 0) return null;
            if (length < buffer.Capacity) return buffer.ToString();
            capacity = checked((int)length + 1);
        }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        [Out] System.Text.StringBuilder path,
        int pathLength,
        uint flags);
}
