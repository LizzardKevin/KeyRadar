using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.Input;

public sealed class HotkeyObservationState
{
    private readonly HashSet<int> _pressedModifierKeys = [];

    public HotkeyGesture? Process(int virtualKey, bool isKeyDown)
    {
        if (TryGetModifier(virtualKey, out _))
        {
            if (isKeyDown)
            {
                _pressedModifierKeys.Add(virtualKey);
            }
            else
            {
                _pressedModifierKeys.Remove(virtualKey);
            }

            return null;
        }

        if (!isKeyDown)
        {
            return null;
        }

        var modifiers = _pressedModifierKeys.Aggregate(
            HotkeyModifiers.None,
            (current, key) => TryGetModifier(key, out var modifier) ? current | modifier : current);
        if (modifiers == HotkeyModifiers.None || !TryGetPrimaryKey(virtualKey, out var primaryKey))
        {
            return null;
        }

        return new HotkeyGesture(modifiers, primaryKey);
    }

    private static bool TryGetModifier(int virtualKey, out HotkeyModifiers modifier)
    {
        modifier = virtualKey switch
        {
            0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
            0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,
            0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
            0x5B or 0x5C => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };

        return modifier != HotkeyModifiers.None;
    }

    private static bool TryGetPrimaryKey(int virtualKey, out string key)
    {
        if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
        {
            key = ((char)virtualKey).ToString();
            return true;
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            key = $"F{virtualKey - 0x6F}";
            return true;
        }

        key = virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            _ => string.Empty,
        };

        return key.Length > 0;
    }
}
