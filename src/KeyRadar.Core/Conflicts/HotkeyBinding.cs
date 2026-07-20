using KeyRadar.Shortcuts;

namespace KeyRadar.Conflicts;

public sealed record ShortcutBinding(
    string ApplicationId,
    ShortcutGesture Gesture,
    ShortcutScope Scope,
    OwnershipConfidence Confidence);
