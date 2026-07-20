using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.DeepConfirmation;

public static class NativeHotkeyMapper
{
    public static bool TryMap(HotkeyGesture gesture, out uint virtualKey, out uint modifiers)
    {
        modifiers = 0;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) modifiers |= 0x0001;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) modifiers |= 0x0002;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) modifiers |= 0x0004;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Windows)) modifiers |= 0x0008;

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
            "BrowserBack" => 0xA6,
            "BrowserForward" => 0xA7,
            "BrowserRefresh" => 0xA8,
            "BrowserStop" => 0xA9,
            "BrowserSearch" => 0xAA,
            "BrowserFavorites" => 0xAB,
            "BrowserHome" => 0xAC,
            "VolumeMute" => 0xAD,
            "VolumeDown" => 0xAE,
            "VolumeUp" => 0xAF,
            "MediaNextTrack" => 0xB0,
            "MediaPreviousTrack" => 0xB1,
            "MediaStop" => 0xB2,
            "MediaPlayPause" => 0xB3,
            "LaunchMail" => 0xB4,
            "LaunchMediaSelect" => 0xB5,
            "LaunchApp1" => 0xB6,
            "LaunchApp2" => 0xB7,
            _ => 0,
        };
        return virtualKey != 0;
    }
}
