using KeyRadar.Hotkeys;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Keeps raw RegisterHotKey occupancy telemetry out of the normal inventory
/// when a modifier-free special virtual key has no attribution. These keys
/// commonly report occupied even when no unfocused-desktop shortcut exists.
/// </summary>
public static class UnknownHotkeyDisplayPolicy
{
    private static readonly HashSet<string> NoisySpecialVirtualKeys = new(StringComparer.Ordinal)
    {
        "BrowserBack", "BrowserForward", "BrowserRefresh", "BrowserStop", "BrowserSearch", "BrowserFavorites", "BrowserHome",
        "VolumeMute", "VolumeDown", "VolumeUp",
        "MediaNextTrack", "MediaPreviousTrack", "MediaStop", "MediaPlayPause",
        "LaunchMail", "LaunchMediaSelect", "LaunchApp1", "LaunchApp2",
        "Backspace", "Tab", "Enter", "Esc", "Space", "PageUp", "PageDown", "End", "Home",
        "Left", "Up", "Right", "Down", "Insert", "Delete",
    };

    public static bool ShouldInclude(DiscoveredHotkey item) =>
        item.Ownership != HotkeyOwnershipStatus.OccupiedOwnerUnknown ||
        item.Availability != HotkeyProbeAvailability.Occupied ||
        !ShouldSuppress(item.Gesture);

    public static bool ShouldSuppress(HotkeyGesture gesture) =>
        gesture.Modifiers == HotkeyModifiers.None &&
        (IsFunctionVirtualKey(gesture.Key) || NoisySpecialVirtualKeys.Contains(gesture.Key));

    private static bool IsFunctionVirtualKey(string key) =>
        key.Length is >= 2 and <= 3 &&
        key[0] == 'F' &&
        int.TryParse(key.AsSpan(1), out var number) &&
        number is >= 1 and <= 24;
}
