using KeyRadar.Conflicts;
using KeyRadar.Shortcuts;

namespace KeyRadar.Rules;

public sealed record ShortcutRule(
    ShortcutGesture Gesture,
    string Function,
    ShortcutScope Scope = ShortcutScope.Application,
    OwnershipConfidence Confidence = OwnershipConfidence.SystemKnown)
{
    public IReadOnlyList<string> Sources { get; init; } = [];

    public RuleOrigin Origin { get; init; } = RuleOrigin.BuiltIn;
}
