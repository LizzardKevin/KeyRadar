namespace KeyRadar.Rules;

public sealed record ApplicationVariantRule(
    string ApplicationId,
    string VariantId,
    LocalizedText DisplayName,
    ApplicationMatchRule Match,
    IReadOnlyList<HotkeyRule> Hotkeys)
{
    public IReadOnlyList<ConfigurationSourceRule> ConfigurationSources { get; init; } = [];

    // This is assigned only by signed official packs or the explicit Debug pack loader.
    // It is deliberately not serialized by the local pack writer.
    public bool IsConfigurationReadAuthorized { get; init; }
}
