using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.DeepConfirmation;

public sealed record DeepConfirmationRequest(
    int ProcessId,
    HotkeyGesture Target,
    TimeSpan Timeout);
