using System.Globalization;

namespace KeyRadar.Shortcuts;

public readonly record struct ShortcutGesture(ShortcutModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, ShortcutModifiers> ModifierAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["alt"] = ShortcutModifiers.Alt,
            ["option"] = ShortcutModifiers.Alt,
            ["ctrl"] = ShortcutModifiers.Control,
            ["control"] = ShortcutModifiers.Control,
            ["cmd"] = ShortcutModifiers.Windows,
            ["command"] = ShortcutModifiers.Windows,
            ["shift"] = ShortcutModifiers.Shift,
            ["win"] = ShortcutModifiers.Windows,
            ["windows"] = ShortcutModifiers.Windows,
        };

    private static readonly Dictionary<string, string> NamedKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["backspace"] = "Backspace",
            ["delete"] = "Delete",
            ["down"] = "Down",
            ["end"] = "End",
            ["enter"] = "Enter",
            ["esc"] = "Esc",
            ["escape"] = "Esc",
            ["home"] = "Home",
            ["insert"] = "Insert",
            ["left"] = "Left",
            ["pagedown"] = "PageDown",
            ["pageup"] = "PageUp",
            ["printscreen"] = "PrintScreen",
            ["right"] = "Right",
            ["space"] = "Space",
            ["tab"] = "Tab",
            ["up"] = "Up",
        };

    public static ShortcutGesture Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 1 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new FormatException("A shortcut must contain non-empty keys separated by '+'.");
        }

        var modifiers = ShortcutModifiers.None;
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
                throw new FormatException("A shortcut must contain exactly one primary key.");
            }

            primaryKey = NormalizePrimaryKey(part);
        }

        if (primaryKey is null)
        {
            throw new FormatException("A shortcut must contain exactly one primary key.");
        }

        return new ShortcutGesture(modifiers, primaryKey);
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        AddModifier(parts, ShortcutModifiers.Control, "Ctrl");
        AddModifier(parts, ShortcutModifiers.Shift, "Shift");
        AddModifier(parts, ShortcutModifiers.Windows, "Win");
        AddModifier(parts, ShortcutModifiers.Alt, "Alt");
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

    private void AddModifier(List<string> parts, ShortcutModifiers modifier, string displayName)
    {
        if (Modifiers.HasFlag(modifier))
        {
            parts.Add(displayName);
        }
    }
}
