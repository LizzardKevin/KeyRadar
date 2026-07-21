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
    string Evidence,
    string? OwnerIdentity = null,
    string? VariantId = null,
    string? CommandId = null);

public static class LocalConfigurationOverridePolicy
{
    /// <summary>
    /// Current local configuration supersedes static defaults (official and suspected) only for
    /// the same running owner/variant command. User declarations, hardware mappings and
    /// RegisterHotKey diagnostics are separate evidence streams and are never filtered here.
    /// </summary>
    public static IReadOnlyList<RunningRuleHotkey> FilterStaticDefaults(
        IEnumerable<RunningRuleHotkey> rules,
        IEnumerable<LocalConfigurationHotkey> localConfigurations)
    {
        var configured = localConfigurations
            .Where(item => !string.IsNullOrWhiteSpace(item.CommandId))
            .Select(item => OverrideIdentity(item.OwnerIdentity ?? item.ApplicationId, item.VariantId, item.CommandId!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rules.Where(rule => !IsStaticDefault(rule.Confidence) ||
            string.IsNullOrWhiteSpace(rule.CommandId) ||
            !configured.Contains(OverrideIdentity(rule.OwnerIdentity ?? rule.ApplicationId, rule.VariantId, rule.CommandId))).ToArray();
    }

    private static string OverrideIdentity(string ownerIdentity, string? variantId, string commandId) =>
        $"{ownerIdentity}\n{variantId ?? string.Empty}\n{commandId}";

    private static bool IsStaticDefault(OwnershipConfidence confidence) =>
        confidence is OwnershipConfidence.OfficialDefault or OwnershipConfidence.Suspected;
}

/// <summary>
/// The presentation-safe hotkey inputs for the current desktop state. A trusted
/// local mapping replaces only its matching static default, while raw rules and
/// probes remain available to diagnostic code outside this projection.
/// </summary>
public sealed record CurrentEffectiveHotkeySet(
    IReadOnlyList<RunningRuleHotkey> Rules,
    IReadOnlyList<LocalConfigurationHotkey> LocalConfigurations,
    IReadOnlyList<RunningRuleHotkey> DiagnosticRules);

public static class CurrentEffectiveHotkeyProjection
{
    public static CurrentEffectiveHotkeySet Project(
        IEnumerable<RunningRuleHotkey> rules,
        IEnumerable<LocalConfigurationHotkey> localConfigurations)
    {
        var rawRules = rules.ToArray();
        var configured = localConfigurations.ToArray();
        return new CurrentEffectiveHotkeySet(
            LocalConfigurationOverridePolicy.FilterStaticDefaults(rawRules, configured),
            configured,
            rawRules);
    }
}

public sealed record DiscoveredHotkey(
    HotkeyGesture Gesture,
    string Function,
    HotkeyScope Scope,
    HotkeyProbeAvailability? Availability,
    HotkeyOwnershipStatus Ownership,
    IReadOnlyList<string> Owners,
    HotkeyConflictStatus Conflict,
    IReadOnlyList<string> Evidence,
    DateTimeOffset? ScannedAtUtc,
    IReadOnlyList<string>? EvidenceOwnerIdentities = null);

public static class HotkeyEvidenceMerger
{
    public static IReadOnlyList<DiscoveredHotkey> Merge(
        IReadOnlyList<HotkeyProbeResult> probes,
        IReadOnlyList<LocalConfigurationHotkey> localConfigurations,
        IReadOnlyList<HardwareProfileDescriptor> hardwareProfiles,
        IReadOnlyList<RunningRuleHotkey> runningRules)
    {
        var latestProbes = HotkeyProbeSelectionPolicy.SelectLatestByGesture(probes);
        var activeHardwareProfiles = hardwareProfiles
            .Where(profile => profile.Status == HardwareProfileReadStatus.Active)
            .Select(profile => profile with
            {
                Mappings = profile.Mappings.Where(mapping => mapping.ParticipatesInConflict).ToArray(),
            })
            .Where(profile => profile.Mappings.Count > 0)
            .ToArray();
        var gestures = latestProbes.Select(item => item.Gesture)
            .Concat(localConfigurations.Select(item => item.Gesture))
            .Concat(activeHardwareProfiles.SelectMany(profile => profile.Mappings)
                .Where(mapping => mapping.TargetGesture is not null)
                .Select(mapping => mapping.TargetGesture!.Value))
            .Concat(runningRules.Select(item => item.Gesture))
            .Distinct()
            .OrderBy(gesture => gesture.ToString(), StringComparer.Ordinal)
            .ToArray();

        return gestures.Select(gesture => MergeGesture(
            gesture,
            latestProbes.FirstOrDefault(item => item.Gesture == gesture),
            localConfigurations.Where(item => item.Gesture == gesture).ToArray(),
            activeHardwareProfiles.Where(profile => profile.Mappings.Any(mapping => mapping.TargetGesture == gesture)).ToArray(),
            runningRules.Where(item => item.Gesture == gesture).ToArray())).ToArray();
    }

    private static DiscoveredHotkey MergeGesture(
        HotkeyGesture gesture,
        HotkeyProbeResult? probe,
        IReadOnlyList<LocalConfigurationHotkey> local,
        IReadOnlyList<HardwareProfileDescriptor> hardware,
        IReadOnlyList<RunningRuleHotkey> rules)
    {
        // A process/variant identity tells us where evidence came from, but
        // it is not a distinct logical application owner. Keeping that
        // distinction prevents two processes (or ambiguous variants) of the
        // same application from conflicting with themselves.
        var owners = DistinctSortedIdentifiers(local.Select(item => item.ApplicationId)
            .Concat(hardware.Select(item => item.SoftwareId))
            .Concat(rules.Select(item => item.ApplicationId)));
        var evidenceOwnerIdentities = DistinctSortedIdentifiers(local
            .Select(item => item.OwnerIdentity ?? item.ApplicationId)
            .Concat(rules.Select(item => item.OwnerIdentity ?? item.ApplicationId)));
        var evidence = local.Select(item => item.Evidence)
            .Concat(hardware.Select(item => item.Evidence))
            .Concat(rules.Select(item => item.Evidence))
            .Append(probe is null ? null : "RegisterHotKey 占用探测")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            // Attributable evidence is ordinal-sorted; the RegisterHotKey
            // diagnostic is always the final display item.
            .OrderBy(item => item.StartsWith("RegisterHotKey", StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(item => item, StringComparer.Ordinal)
            .ToArray();

        var localCandidateCount = local
            .Select(item => item.ApplicationId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var ruleCandidateCount = rules
            .Select(item => item.ApplicationId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var primary = local
            .OrderBy(item => ScopeRank(item.Scope))
            .ThenBy(item => item.Function, StringComparer.Ordinal)
            .ThenBy(item => item.Evidence, StringComparer.Ordinal)
            .ThenBy(item => item.ApplicationId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.OwnerIdentity ?? item.ApplicationId, StringComparer.Ordinal)
            .FirstOrDefault();
        var rule = rules
            .OrderBy(item => ScopeRank(item.Scope))
            .ThenByDescending(item => ConfidenceRank(item.Confidence))
            .ThenBy(item => item.Function, StringComparer.Ordinal)
            .ThenBy(item => item.Evidence, StringComparer.Ordinal)
            .ThenBy(item => item.ApplicationId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.OwnerIdentity ?? item.ApplicationId, StringComparer.Ordinal)
            .FirstOrDefault();
        var ownership = localCandidateCount == 1
            ? HotkeyOwnershipStatus.LocalConfigurationFound
            : localCandidateCount > 1
                ? HotkeyOwnershipStatus.PossibleOwner
            : hardware.Count > 0
                ? hardware.All(profile => profile.IsUserDeclared)
                    ? HotkeyOwnershipStatus.PossibleOwner
                    : HotkeyOwnershipStatus.HardwareMappingFound
                : ruleCandidateCount == 1
                    ? OwnershipForRule(rule!.Confidence)
                    : ruleCandidateCount > 1
                        ? HotkeyOwnershipStatus.PossibleOwner
                        : probe?.Availability == HotkeyProbeAvailability.Occupied
                            ? HotkeyOwnershipStatus.OccupiedOwnerUnknown
                            : HotkeyOwnershipStatus.Unknown;

        var localOwners = local
            .Select(item => item.ApplicationId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var ruleOwners = rules
            .Select(item => item.ApplicationId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exactHardware = hardware.Where(profile => !profile.IsUserDeclared).ToArray();
        var declaredHardware = hardware.Where(profile => profile.IsUserDeclared).ToArray();
        var conflict = exactHardware.Length > 0 && (local.Count > 0 || rules.Count > 0)
            ? HotkeyConflictStatus.HardwareMappingCollision
            : declaredHardware.Length > 0 && (local.Count > 0 || rules.Count > 0)
                ? HotkeyConflictStatus.PossibleInterception
            : localOwners.Length > 1
                ? HotkeyConflictStatus.DefiniteConflict
                : localOwners.Length > 0 && ruleOwners.Any(owner => !localOwners.Contains(owner, StringComparer.OrdinalIgnoreCase))
                    ? HotkeyConflictStatus.PossibleInterception
                    : ruleOwners.Length > 1
                    ? HotkeyConflictStatus.PossibleInterception
                    : HotkeyConflictStatus.None;
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
            probe?.ScannedAtUtc,
            evidenceOwnerIdentities);
    }

    private static HotkeyOwnershipStatus OwnershipForRule(OwnershipConfidence confidence) => confidence switch
    {
        OwnershipConfidence.SystemKnown => HotkeyOwnershipStatus.WindowsKnown,
        OwnershipConfidence.OfficialDefault => HotkeyOwnershipStatus.OfficialDefault,
        OwnershipConfidence.Confirmed or OwnershipConfidence.Corroborated => HotkeyOwnershipStatus.Confirmed,
        OwnershipConfidence.LocalConfiguration => HotkeyOwnershipStatus.LocalConfigurationFound,
        OwnershipConfidence.HardwareMapping => HotkeyOwnershipStatus.HardwareMappingFound,
        OwnershipConfidence.UserDeclared or OwnershipConfidence.Suspected or OwnershipConfidence.Unknown => HotkeyOwnershipStatus.PossibleOwner,
        _ => HotkeyOwnershipStatus.PossibleOwner,
    };

    private static int ScopeRank(HotkeyScope scope) => scope switch
    {
        HotkeyScope.WindowsSystem => 0,
        HotkeyScope.Global => 1,
        HotkeyScope.Background => 2,
        HotkeyScope.Foreground => 3,
        _ => int.MaxValue,
    };

    private static int ConfidenceRank(OwnershipConfidence confidence) => confidence switch
    {
        OwnershipConfidence.Confirmed => 6,
        OwnershipConfidence.Corroborated => 5,
        OwnershipConfidence.LocalConfiguration => 4,
        OwnershipConfidence.HardwareMapping => 3,
        OwnershipConfidence.SystemKnown => 2,
        OwnershipConfidence.OfficialDefault => 1,
        _ => 0,
    };

    private static IReadOnlyList<string> DistinctSortedIdentifiers(IEnumerable<string> values) => values
        .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
        // Canonicalize case-insensitive duplicates before sorting, rather than
        // retaining the first input spelling.
        .Select(group => group.OrderBy(value => value, StringComparer.Ordinal).First())
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ThenBy(value => value, StringComparer.Ordinal)
        .ToArray();
}
