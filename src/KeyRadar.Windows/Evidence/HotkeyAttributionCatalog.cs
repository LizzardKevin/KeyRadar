using KeyRadar.Hotkeys;
using KeyRadar.Windows.Configuration;
using KeyRadar.Windows.Hardware;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Produces one ownership result per discovered gesture. The RegisterHotKey
/// probe starts without an owner; safe evidence from the current session can
/// replace that unknown owner without ever guessing a process.
/// </summary>
public sealed class HotkeyAttributionCatalog
{
    private readonly IReadOnlyDictionary<HotkeyGesture, DiscoveredHotkey> _byGesture;

    private HotkeyAttributionCatalog(
        IReadOnlyList<DiscoveredHotkey> diagnosticItems,
        IReadOnlyList<HotkeyProbeResult> probes)
    {
        DiagnosticItems = diagnosticItems;
        _byGesture = diagnosticItems.ToDictionary(item => item.Gesture);
        Items = diagnosticItems
            .Where(IsDiscoverable)
            .Where(UnknownHotkeyDisplayPolicy.ShouldInclude)
            .ToArray();
        UnknownProbeGestures = Items
            .Where(item => item.Ownership == HotkeyOwnershipStatus.OccupiedOwnerUnknown)
            .Where(item => item.Availability == HotkeyProbeAvailability.Occupied)
            .Select(item => item.Gesture)
            .ToHashSet();
        ActionableUnknownProbes = HotkeyProbeSelectionPolicy.SelectLatestByGesture(probes)
            .Where(probe => probe.Availability == HotkeyProbeAvailability.Occupied)
            .Where(probe => UnknownProbeGestures.Contains(probe.Gesture))
            .ToArray();
    }

    /// <summary>
    /// Curated inventory entries. A raw registration probe contributes an entry
    /// only when it found an occupied, unattributed gesture; availability and
    /// probe failures remain diagnostics unless independent evidence identifies
    /// a real hotkey.
    /// </summary>
    public IReadOnlyList<DiscoveredHotkey> Items { get; }

    /// <summary>All merged entries, including scan-only probe telemetry.</summary>
    public IReadOnlyList<DiscoveredHotkey> DiagnosticItems { get; }

    public IReadOnlySet<HotkeyGesture> UnknownProbeGestures { get; }

    public IReadOnlyList<HotkeyProbeResult> ActionableUnknownProbes { get; }
    public bool TryGet(HotkeyGesture gesture, out DiscoveredHotkey item) =>
        _byGesture.TryGetValue(gesture, out item!);

    public static HotkeyAttributionCatalog Create(
        IReadOnlyList<HotkeyProbeResult> probes,
        IReadOnlyList<RunningRuleHotkey> runningRules,
        IReadOnlyList<LocalConfigurationHotkey> localConfigurations,
        IReadOnlyList<HardwareProfileDescriptor> hardwareProfiles) =>
        new(HotkeyEvidenceMerger.Merge(probes, localConfigurations, hardwareProfiles, runningRules), probes);

    private static bool IsDiscoverable(DiscoveredHotkey item) =>
        item.Ownership is not HotkeyOwnershipStatus.Unknown ||
        item.Availability == HotkeyProbeAvailability.Occupied;

}
