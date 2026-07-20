using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.DeepConfirmation;

namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Assigns each curated attribution entry to exactly one normal inventory
/// group. The entry itself remains intact so secondary owners and all evidence
/// are still available to the presentation layer.
/// </summary>
public enum HotkeyInventoryGroup
{
    ForegroundApplication,
    ActiveHardwareProfile,
    BackgroundApplication,
    WindowsSystem,
    UnknownOccupied,
}

public sealed record HotkeyInventoryOwner(
    string OwnerId,
    HotkeyInventoryGroup Group,
    string? GroupId = null,
    string? DisplayName = null,
    DeepConfirmationEvidenceKind Evidence = DeepConfirmationEvidenceKind.OfficialRule,
    HotkeyGesture? TargetGesture = null,
    string? DisplayOwnerId = null);

public sealed record ProjectedHotkeyInventoryRow(
    DiscoveredHotkey Item,
    HotkeyInventoryGroup Group,
    string? PrimaryOwnerId,
    string? PrimaryGroupId,
    bool HasActiveHardwareEvidence,
    IReadOnlyList<DeepConfirmationCandidate> DeepConfirmationCandidates);

public static class NormalHotkeyInventoryProjector
{
    public static IReadOnlyList<ProjectedHotkeyInventoryRow> Project(
        IReadOnlyList<DiscoveredHotkey> items,
        IReadOnlyList<HotkeyInventoryOwner> owners)
    {
        var allOwnersById = owners
            .GroupBy(owner => owner.OwnerId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<HotkeyInventoryOwner>)group.ToArray(), StringComparer.OrdinalIgnoreCase);

        return items
            .Select(item => Project(item, allOwnersById))
            .OrderBy(row => row.Item.Gesture.ToString(), StringComparer.Ordinal)
            .ToArray();
    }

    private static ProjectedHotkeyInventoryRow Project(
        DiscoveredHotkey item,
        IReadOnlyDictionary<string, IReadOnlyList<HotkeyInventoryOwner>> allOwnersById)
    {
        var ownerIdentities = (item.EvidenceOwnerIdentities ?? [])
            .Concat(item.Owners)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var knownOwners = ownerIdentities
            .Where(allOwnersById.ContainsKey)
            .SelectMany(owner => allOwnersById[owner])
            .ToArray();
        var primary = knownOwners
            .OrderBy(owner => Rank(owner.Group))
            .ThenBy(owner => owner.OwnerId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var group = primary?.Group ?? (item.Ownership == HotkeyOwnershipStatus.OccupiedOwnerUnknown
            ? HotkeyInventoryGroup.UnknownOccupied
            : item.Scope == HotkeyScope.WindowsSystem
                ? HotkeyInventoryGroup.WindowsSystem
                : HotkeyInventoryGroup.BackgroundApplication);

        var matchingOwners = knownOwners
            .Where(owner => owner.TargetGesture is null || owner.TargetGesture == item.Gesture)
            .ToArray();
        var deepConfirmationCandidates = matchingOwners
            .Select(owner => new DeepConfirmationCandidate(
                owner.DisplayOwnerId ?? owner.OwnerId,
                owner.DisplayName ?? owner.OwnerId,
                owner.Evidence,
                owner.OwnerId))
            // Exact sources appear first. Within an evidence kind, use a total
            // ordinal tuple of application identity, displayed owner, and description.
            .OrderBy(candidate => CandidateEvidenceRank(candidate.Evidence))
            .ThenBy(candidate => candidate.EvidenceIdentity ?? candidate.OwnerId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.EvidenceIdentity ?? candidate.OwnerId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OwnerId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.OwnerId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.Ordinal)
            .DistinctBy(candidate => $"{candidate.EvidenceIdentity ?? candidate.OwnerId}\u001F{candidate.Evidence}", StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProjectedHotkeyInventoryRow(
            item,
            group,
            primary?.OwnerId,
            primary?.GroupId,
            matchingOwners.Any(owner => owner.Group == HotkeyInventoryGroup.ActiveHardwareProfile),
            deepConfirmationCandidates);
    }

    private static int Rank(HotkeyInventoryGroup group) => group switch
    {
        HotkeyInventoryGroup.ForegroundApplication => 0,
        HotkeyInventoryGroup.ActiveHardwareProfile => 1,
        HotkeyInventoryGroup.BackgroundApplication => 2,
        HotkeyInventoryGroup.WindowsSystem => 3,
        HotkeyInventoryGroup.UnknownOccupied => 4,
        _ => int.MaxValue,
    };

    private static int CandidateEvidenceRank(DeepConfirmationEvidenceKind evidence) => evidence switch
    {
        DeepConfirmationEvidenceKind.LocalConfiguration => 0,
        DeepConfirmationEvidenceKind.ActiveHardwareProfile => 1,
        DeepConfirmationEvidenceKind.OfficialRule => 2,
        DeepConfirmationEvidenceKind.UserDeclaredHardwareProfile => 3,
        _ => int.MaxValue,
    };
}
