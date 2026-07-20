using System.Globalization;

namespace KeyRadar.Hotkeys;

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, HotkeyModifiers> ModifierAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["alt"] = HotkeyModifiers.Alt,
            ["option"] = HotkeyModifiers.Alt,
            ["ctrl"] = HotkeyModifiers.Control,
            ["control"] = HotkeyModifiers.Control,
            ["cmd"] = HotkeyModifiers.Windows,
            ["command"] = HotkeyModifiers.Windows,
            ["shift"] = HotkeyModifiers.Shift,
            ["win"] = HotkeyModifiers.Windows,
            ["windows"] = HotkeyModifiers.Windows,
        };

    private static readonly Dictionary<string, string> NamedKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["backspace"] = "Backspace",
            ["browserback"] = "BrowserBack",
            ["browserfavorites"] = "BrowserFavorites",
            ["browserforward"] = "BrowserForward",
            ["browserhome"] = "BrowserHome",
            ["browserrefresh"] = "BrowserRefresh",
            ["browsersearch"] = "BrowserSearch",
            ["browserstop"] = "BrowserStop",
            ["delete"] = "Delete",
            ["down"] = "Down",
            ["end"] = "End",
            ["enter"] = "Enter",
            ["esc"] = "Esc",
            ["escape"] = "Esc",
            ["home"] = "Home",
            ["insert"] = "Insert",
            ["left"] = "Left",
            ["launchapp1"] = "LaunchApp1",
            ["launchapp2"] = "LaunchApp2",
            ["launchmail"] = "LaunchMail",
            ["launchmediaselect"] = "LaunchMediaSelect",
            ["medianexttrack"] = "MediaNextTrack",
            ["mediaplaypause"] = "MediaPlayPause",
            ["mediaprevioustrack"] = "MediaPreviousTrack",
            ["mediastop"] = "MediaStop",
            ["pagedown"] = "PageDown",
            ["pageup"] = "PageUp",
            ["printscreen"] = "PrintScreen",
            ["right"] = "Right",
            ["space"] = "Space",
            ["tab"] = "Tab",
            ["up"] = "Up",
            ["volumedown"] = "VolumeDown",
            ["volumemute"] = "VolumeMute",
            ["volumeup"] = "VolumeUp",
        };

    public static HotkeyGesture Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 1 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new FormatException("A hotkey must contain non-empty keys separated by '+'.");
        }

        var modifiers = HotkeyModifiers.None;
        string? primaryKey = null;

        foreach (var part in parts)
        {
            if (ModifierAliases.TryGetValue(part, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (primaryKey is not null)
            {
                throw new FormatException("A hotkey must contain exactly one primary key.");
            }

            primaryKey = NormalizePrimaryKey(part);
        }

        if (primaryKey is null)
        {
            throw new FormatException("A hotkey must contain exactly one primary key.");
        }

        return new HotkeyGesture(modifiers, primaryKey);
    }

    public static bool TryParse(string? value, out HotkeyGesture gesture)
    {
        try
        {
            if (value is null)
            {
                gesture = default;
                return false;
            }

            gesture = Parse(value);
            return true;
        }
        catch (FormatException)
        {
            gesture = default;
            return false;
        }
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        AddModifier(parts, HotkeyModifiers.Control, "Ctrl");
        AddModifier(parts, HotkeyModifiers.Shift, "Shift");
        AddModifier(parts, HotkeyModifiers.Windows, "Win");
        AddModifier(parts, HotkeyModifiers.Alt, "Alt");
        parts.Add(Key ?? string.Empty);
        return string.Join('+', parts);
    }

    private static string NormalizePrimaryKey(string value)
    {
        if (value.Length == 1)
        {
            return value.ToUpper(CultureInfo.InvariantCulture);
        }

        if (NamedKeys.TryGetValue(value, out var namedKey))
        {
            return namedKey;
        }

        if (value.Length is 2 or 3 &&
            value[0] is 'f' or 'F' &&
            int.TryParse(value.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            return $"F{functionKey}";
        }

        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..].ToLower(CultureInfo.InvariantCulture);
    }

    private void AddModifier(List<string> parts, HotkeyModifiers modifier, string displayName)
    {
        if (Modifiers.HasFlag(modifier))
        {
            parts.Add(displayName);
        }
    }
}
