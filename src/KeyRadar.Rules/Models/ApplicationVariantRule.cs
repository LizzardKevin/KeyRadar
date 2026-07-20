namespace KeyRadar.Rules;

public sealed record ApplicationVariantRule(
    string ApplicationId,
    string VariantId,
    LocalizedText DisplayName,
    ApplicationMatchRule Match,
    IReadOnlyList<HotkeyRule> Hotkeys);
