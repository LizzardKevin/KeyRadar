using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;

namespace KeyRadar.Rules;

public sealed record HotkeyRule(
    HotkeyGesture Gesture,
    LocalizedText Function,
    HotkeyScope Scope = HotkeyScope.Foreground,
    OwnershipConfidence Confidence = OwnershipConfidence.SystemKnown)
{
    public IReadOnlyList<string> Sources { get; init; } = [];
}
