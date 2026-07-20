using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.Hotkeys;

public enum HotkeyProbeAvailability
{
    Occupied,
    AvailableAtScanTime,
    SystemReserved,
    ProbeError,
}

public enum HotkeyProbeMechanism
{
    RegisterHotKeyProbe,
}

public enum HotkeyOwner
{
    Unknown,
}

public sealed record HotkeyProbeResult(
    HotkeyGesture Gesture,
    HotkeyProbeAvailability Availability,
    HotkeyProbeMechanism Mechanism,
    HotkeyOwner Owner,
    DateTimeOffset ScannedAtUtc,
    int? Win32ErrorCode = null);
