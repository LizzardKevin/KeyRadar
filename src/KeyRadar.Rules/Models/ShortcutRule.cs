using KeyRadar.Conflicts;
using KeyRadar.Shortcuts;

namespace KeyRadar.Rules;

public sealed record ShortcutRule(
    ShortcutGesture Gesture,
    string Function,
    ShortcutScope Scope = ShortcutScope.Application,
    OwnershipConfidence Confidence = OwnershipConfidence.SystemKnown);
