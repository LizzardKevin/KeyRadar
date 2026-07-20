namespace KeyRadar.Rules;

public sealed record ApplicationRuleSet(
    string Id,
    string DisplayName,
    IReadOnlyList<string> ExecutableNames,
    IReadOnlyList<ShortcutRule> Shortcuts);
