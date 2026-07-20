namespace KeyRadar.Windows.DeepConfirmation;

/// <summary>Canonicalizes current-session candidates independently of scanner order.</summary>
public static class DeepConfirmationCandidateOrdering
{
    public static IReadOnlyList<DeepConfirmationCandidate> Canonicalize(
        IEnumerable<DeepConfirmationCandidate> candidates) => candidates
        .OrderBy(candidate => EvidenceRank(candidate.Evidence))
        .ThenBy(candidate => candidate.EvidenceIdentity ?? candidate.OwnerId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(candidate => candidate.EvidenceIdentity ?? candidate.OwnerId, StringComparer.Ordinal)
        .ThenBy(candidate => candidate.OwnerId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(candidate => candidate.OwnerId, StringComparer.Ordinal)
        .ThenBy(candidate => candidate.DisplayName, StringComparer.Ordinal)
        .DistinctBy(
            candidate => $"{candidate.EvidenceIdentity ?? candidate.OwnerId}\u001F{candidate.Evidence}",
            StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static int EvidenceRank(DeepConfirmationEvidenceKind evidence) => evidence switch
    {
        DeepConfirmationEvidenceKind.LocalConfiguration => 0,
        DeepConfirmationEvidenceKind.ActiveHardwareProfile => 1,
        DeepConfirmationEvidenceKind.OfficialRule => 2,
        DeepConfirmationEvidenceKind.UserDeclaredHardwareProfile => 3,
        _ => int.MaxValue,
    };
}
