using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Configuration;
using KeyRadar.Windows.Hardware;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Evidence;

public enum HotkeyOwnershipStatus
{
    Confirmed,
    LocalConfigurationFound,
    HardwareMappingFound,
    WindowsKnown,
    OfficialDefault,
    PossibleOwner,
    OccupiedOwnerUnknown,
    Unknown,
}

public enum HotkeyConflictStatus
{
    None,
    DefiniteConflict,
    PossibleInterception,
    ContextualReuse,
    HardwareMappingCollision,
}

public sealed record RunningRuleHotkey(
    string ApplicationId,
    HotkeyGesture Gesture,
    string Function,
    HotkeyScope Scope,
    OwnershipConfidence Confidence,
    string Evidence);

public sealed record DiscoveredHotkey(
    HotkeyGesture Gesture,
    string Function,
    HotkeyScope Scope,
    HotkeyProbeAvailability? Availability,
    HotkeyOwnershipStatus Ownership,
    IReadOnlyList<string> Owners,
    HotkeyConflictStatus Conflict,
    IReadOnlyList<string> Evidence,
    DateTimeOffset? ScannedAtUtc);

public static class HotkeyEvidenceMerger
{
    public static IReadOnlyList<DiscoveredHotkey> Merge(
        IReadOnlyList<HotkeyProbeResult> probes,
        IReadOnlyList<LocalConfigurationHotkey> localConfigurations,
        IReadOnlyList<HardwareProfileDescriptor> hardwareProfiles,
        IReadOnlyList<RunningRuleHotkey> runningRules)
    {
        var gestures = probes.Select(item => item.Gesture)
            .Concat(localConfigurations.Select(item => item.Gesture))
            .Concat(hardwareProfiles.SelectMany(profile => profile.Mappings)
                .Where(mapping => mapping.TargetGesture is not null)
                .Select(mapping => mapping.TargetGesture!.Value))
            .Concat(runningRules.Select(item => item.Gesture))
            .Distinct()
            .OrderBy(gesture => gesture.ToString(), StringComparer.Ordinal)
            .ToArray();

        return gestures.Select(gesture => MergeGesture(
            gesture,
            probes.FirstOrDefault(item => item.Gesture == gesture),
            localConfigurations.Where(item => item.Gesture == gesture).ToArray(),
            hardwareProfiles.Where(profile => profile.Mappings.Any(mapping => mapping.TargetGesture == gesture)).ToArray(),
            runningRules.Where(item => item.Gesture == gesture).ToArray())).ToArray();
    }

    private static DiscoveredHotkey MergeGesture(
        HotkeyGesture gesture,
        HotkeyProbeResult? probe,
        IReadOnlyList<LocalConfigurationHotkey> local,
        IReadOnlyList<HardwareProfileDescriptor> hardware,
        IReadOnlyList<RunningRuleHotkey> rules)
    {
        var owners = local.Select(item => item.ApplicationId)
            .Concat(hardware.Select(item => item.SoftwareId))
            .Concat(rules.Select(item => item.ApplicationId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evidence = local.Select(item => item.Evidence)
            .Concat(hardware.Select(item => item.Evidence))
            .Concat(rules.Select(item => item.Evidence))
            .Append(probe is null ? null : "RegisterHotKey 占用探测")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var ownership = local.Count > 0
            ? HotkeyOwnershipStatus.LocalConfigurationFound
            : hardware.Count > 0
                ? HotkeyOwnershipStatus.HardwareMappingFound
                : rules.Count == 1
                    ? rules[0].Confidence == OwnershipConfidence.SystemKnown
                        ? HotkeyOwnershipStatus.WindowsKnown
                        : HotkeyOwnershipStatus.OfficialDefault
                    : rules.Count > 1
                        ? HotkeyOwnershipStatus.PossibleOwner
                        : probe?.Availability == HotkeyProbeAvailability.Occupied
                            ? HotkeyOwnershipStatus.OccupiedOwnerUnknown
                            : HotkeyOwnershipStatus.Unknown;

        var conflict = hardware.Count > 0 && (local.Count > 0 || rules.Count > 0)
            ? HotkeyConflictStatus.HardwareMappingCollision
            : local.Select(item => item.ApplicationId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
                ? HotkeyConflictStatus.DefiniteConflict
                : rules.Select(item => item.ApplicationId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
                    ? HotkeyConflictStatus.PossibleInterception
                    : HotkeyConflictStatus.None;
        var primary = local.FirstOrDefault();
        var rule = rules.FirstOrDefault();
        var function = primary?.Function ?? rule?.Function ?? (hardware.Count > 0 ? "硬件映射" : "功能未知");
        var scope = primary?.Scope ?? rule?.Scope ?? (hardware.Count > 0 ? HotkeyScope.Global : HotkeyScope.Global);

        return new DiscoveredHotkey(
            gesture,
            function,
            scope,
            probe?.Availability,
            ownership,
            owners,
            conflict,
            evidence,
            probe?.ScannedAtUtc);
    }
}
