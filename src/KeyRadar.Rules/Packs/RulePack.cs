namespace KeyRadar.Rules.Packs;

public sealed record RulePack(
    string PackId,
    string Version,
    IReadOnlyList<ApplicationVariantRule> Variants);
