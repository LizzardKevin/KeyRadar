namespace KeyRadar.Rules;

public enum VariantEvidenceState
{
    Match,
    Missing,
    Mismatch,
}

public sealed record VariantMatchEvidence(
    string Signal,
    VariantEvidenceState State,
    string Description,
    int Score);
