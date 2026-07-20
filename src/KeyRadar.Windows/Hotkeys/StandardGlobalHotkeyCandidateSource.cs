using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.Hotkeys;

public static class StandardGlobalHotkeyCandidateSource
{
    private static readonly HotkeyModifiers[] ModifierSets =
    [
        HotkeyModifiers.Control,
        HotkeyModifiers.Alt,
        HotkeyModifiers.Shift,
        HotkeyModifiers.Control | HotkeyModifiers.Alt,
        HotkeyModifiers.Control | HotkeyModifiers.Shift,
        HotkeyModifiers.Alt | HotkeyModifiers.Shift,
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
    ];

    private static readonly string[] FunctionalKeys =
    [
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
        "BrowserBack", "BrowserForward", "BrowserRefresh", "BrowserStop", "BrowserSearch",
        "BrowserFavorites", "BrowserHome", "VolumeMute", "VolumeDown", "VolumeUp",
        "MediaNextTrack", "MediaPreviousTrack", "MediaStop", "MediaPlayPause",
        "LaunchMail", "LaunchMediaSelect", "LaunchApp1", "LaunchApp2",
    ];

    private static readonly string[] ModifiedKeys =
    [
        .. Enumerable.Range('A', 26).Select(value => ((char)value).ToString()),
        .. Enumerable.Range(0, 10).Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        .. FunctionalKeys,
        "Backspace", "Tab", "Enter", "Esc", "Space", "PageUp", "PageDown", "End", "Home",
        "Left", "Up", "Right", "Down", "PrintScreen", "Insert", "Delete",
    ];

    public static IReadOnlyList<HotkeyGesture> Create()
    {
        var candidates = new List<HotkeyGesture>(ModifierSets.Length * ModifiedKeys.Length + FunctionalKeys.Length);
        candidates.AddRange(FunctionalKeys.Select(key => new HotkeyGesture(HotkeyModifiers.None, key)));
        foreach (var modifiers in ModifierSets)
        {
            foreach (var key in ModifiedKeys)
            {
                if (modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Alt) && key == "Delete")
                {
                    continue;
                }

                candidates.Add(new HotkeyGesture(modifiers, key));
            }
        }

        return candidates;
    }
}
