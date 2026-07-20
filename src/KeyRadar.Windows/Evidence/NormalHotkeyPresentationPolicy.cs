using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Keeps raw probe telemetry out of the curated hotkey inventory. Diagnostics
/// remain the authoritative surface for probe availability and failures.
/// </summary>
public static class NormalHotkeyPresentationPolicy
{
    public static bool ShouldShowAvailability(HotkeyProbeAvailability? availability) =>
        availability == HotkeyProbeAvailability.Occupied;
}
