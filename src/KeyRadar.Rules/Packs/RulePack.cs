namespace KeyRadar.Rules.Packs;

public enum RulePackTrust
{
    SignedOfficial,
    UnsignedLocal,
}

public sealed record RulePack(
    string PackId,
    string Version,
    IReadOnlyList<ApplicationVariantRule> Variants,
    RulePackTrust Trust = RulePackTrust.SignedOfficial);
