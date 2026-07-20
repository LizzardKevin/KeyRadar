using KeyRadar.Shortcuts;

namespace KeyRadar.Windows.DeepConfirmation;

public static class NativeHotkeyMapper
{
    public static bool TryMap(ShortcutGesture gesture, out uint virtualKey, out uint modifiers)
    {
        modifiers = 0;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Alt)) modifiers |= 0x0001;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Control)) modifiers |= 0x0002;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Shift)) modifiers |= 0x0004;
        if (gesture.Modifiers.HasFlag(ShortcutModifiers.Windows)) modifiers |= 0x0008;

        return TryMapVirtualKey(gesture.Key, out virtualKey);
    }

    private static bool TryMapVirtualKey(string key, out uint virtualKey)
    {
        if (key.Length == 1)
        {
            var character = char.ToUpperInvariant(key[0]);
            if (character is >= '0' and <= '9' or >= 'A' and <= 'Z')
            {
                virtualKey = character;
                return true;
            }
        }

        if (key.StartsWith('F') &&
            int.TryParse(key.AsSpan(1), out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x6F + functionKey);
            return true;
        }

        virtualKey = key switch
        {
            "Backspace" => 0x08,
            "Tab" => 0x09,
            "Enter" => 0x0D,
            "Esc" => 0x1B,
            "Space" => 0x20,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "End" => 0x23,
            "Home" => 0x24,
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            "PrintScreen" => 0x2C,
            "Insert" => 0x2D,
            "Delete" => 0x2E,
            _ => 0,
        };
        return virtualKey != 0;
    }
}
