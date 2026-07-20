using KeyRadar.Hotkeys;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Selects one probe per gesture without trusting source enumeration order.
/// For equally recent observations, the conservative order is Occupied,
/// SystemReserved, ProbeError, then AvailableAtScanTime. Error code,
/// mechanism, and owner provide stable final tie-breakers.
/// </summary>
public static class HotkeyProbeSelectionPolicy
{
    public static IReadOnlyList<HotkeyProbeResult> SelectLatestByGesture(
        IEnumerable<HotkeyProbeResult> probes) =>
        probes
            .GroupBy(probe => probe.Gesture)
            .Select(SelectLatest)
            .OrderBy(probe => probe.Gesture.ToString(), StringComparer.Ordinal)
            .ToArray();

    public static HotkeyProbeResult SelectLatest(IEnumerable<HotkeyProbeResult> probes) =>
        probes
            .OrderByDescending(probe => probe.ScannedAtUtc)
            .ThenBy(probe => AvailabilityRank(probe.Availability))
            .ThenBy(probe => probe.Win32ErrorCode ?? int.MinValue)
            .ThenBy(probe => probe.Mechanism)
            .ThenBy(probe => probe.Owner)
            .First();

    private static int AvailabilityRank(HotkeyProbeAvailability availability) => availability switch
    {
        HotkeyProbeAvailability.Occupied => 0,
        HotkeyProbeAvailability.SystemReserved => 1,
        HotkeyProbeAvailability.ProbeError => 2,
        HotkeyProbeAvailability.AvailableAtScanTime => 3,
        _ => int.MaxValue,
    };
}
