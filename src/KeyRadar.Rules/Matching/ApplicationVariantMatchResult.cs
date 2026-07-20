namespace KeyRadar.Rules;

public enum VariantMatchKind
{
    None,
    Suspected,
    Exact,
    Ambiguous,
}

public sealed record ApplicationVariantCandidate(
    ApplicationVariantRule Variant,
    int Score,
    IReadOnlyList<VariantMatchEvidence> Evidence);

public sealed record ApplicationVariantMatchResult(
    VariantMatchKind Kind,
    ApplicationVariantRule? Selected,
    IReadOnlyList<ApplicationVariantCandidate> Candidates)
{
    public static ApplicationVariantMatchResult NoMatch { get; } =
        new(VariantMatchKind.None, null, []);
}
