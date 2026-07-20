namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Stable provenance for evidence produced by a particular running process
/// and rule-pack variant. Application IDs are display identities, not unique
/// runtime owners.
/// </summary>
public sealed record RunningApplicationEvidenceIdentity(
    int ProcessId,
    string ApplicationId,
    string VariantId)
{
    public string Value => $"{ProcessId}\u001F{ApplicationId}\u001F{VariantId}";
}
