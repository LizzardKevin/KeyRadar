using KeyRadar.Shortcuts;

namespace KeyRadar.Windows.DeepConfirmation;

public sealed record DeepConfirmationRequest(
    int ProcessId,
    ShortcutGesture Target,
    TimeSpan Timeout);
