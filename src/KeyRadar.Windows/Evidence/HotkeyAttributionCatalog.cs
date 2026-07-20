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

    private HotkeyAttributionCatalog(IReadOnlyList<DiscoveredHotkey> items)
    {
        Items = items;
        _byGesture = items.ToDictionary(item => item.Gesture);
        UnknownProbeGestures = items
            .Where(item => item.Ownership is HotkeyOwnershipStatus.OccupiedOwnerUnknown or HotkeyOwnershipStatus.Unknown)
            .Where(item => item.Availability is not HotkeyProbeAvailability.AvailableAtScanTime)
            .Select(item => item.Gesture)
            .ToHashSet();
    }

    public IReadOnlyList<DiscoveredHotkey> Items { get; }

    public IReadOnlySet<HotkeyGesture> UnknownProbeGestures { get; }

    public bool TryGet(HotkeyGesture gesture, out DiscoveredHotkey item) =>
        _byGesture.TryGetValue(gesture, out item!);

    public static HotkeyAttributionCatalog Create(
        IReadOnlyList<HotkeyProbeResult> probes,
        IReadOnlyList<RunningRuleHotkey> runningRules,
        IReadOnlyList<LocalConfigurationHotkey> localConfigurations,
        IReadOnlyList<HardwareProfileDescriptor> hardwareProfiles) =>
        new(HotkeyEvidenceMerger.Merge(probes, localConfigurations, hardwareProfiles, runningRules));
}
