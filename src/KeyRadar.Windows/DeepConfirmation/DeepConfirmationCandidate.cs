namespace KeyRadar.Windows.DeepConfirmation;

public enum DeepConfirmationEvidenceKind
{
    OfficialRule,
    LocalConfiguration,
    ActiveHardwareProfile,
    UserDeclaredHardwareProfile,
}

/// <summary>
/// Preserves the evidence-backed owner candidates used by attribution and diagnostics.
/// </summary>
public sealed record DeepConfirmationCandidate(
    string OwnerId,
    string DisplayName,
    DeepConfirmationEvidenceKind Evidence,
    string? EvidenceIdentity = null);
