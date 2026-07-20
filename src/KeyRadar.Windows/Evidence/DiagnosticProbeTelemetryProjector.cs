using KeyRadar.Diagnostics;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Projects the complete, latest RegisterHotKey probe set into the safe
/// diagnostic schema. This intentionally uses diagnostic catalog entries
/// rather than the curated inventory, so suppressed probes remain exportable.
/// </summary>
public static class DiagnosticProbeTelemetryProjector
{
    public static IReadOnlyList<DiagnosticProbeTelemetry> Project(
        IEnumerable<HotkeyProbeResult> probes,
        IReadOnlyList<DiscoveredHotkey> diagnosticItems)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(diagnosticItems);

        var diagnosticStatusByGesture = diagnosticItems.ToDictionary(
            item => item.Gesture,
            item => item.Ownership);

        return HotkeyProbeSelectionPolicy.SelectLatestByGesture(probes)
            .Select(probe => new DiagnosticProbeTelemetry(
                probe.Gesture.ToString(),
                probe.Availability.ToString(),
                probe.Mechanism.ToString(),
                probe.ScannedAtUtc,
                probe.Win32ErrorCode,
                probe.Owner.ToString(),
                diagnosticStatusByGesture.TryGetValue(probe.Gesture, out var status)
                    ? status.ToString()
                    : HotkeyOwnershipStatus.Unknown.ToString()))
            .ToArray();
    }
}
