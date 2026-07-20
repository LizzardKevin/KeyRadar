using KeyRadar.Hotkeys;

namespace KeyRadar.Conflicts;

public sealed record HotkeyBinding(
    string ApplicationId,
    HotkeyGesture Gesture,
    HotkeyScope Scope,
    OwnershipConfidence Confidence);
