namespace KeyRadar.Windows.Hotkeys;

public sealed record HotkeyRegistrationResult(
    HotkeyRegistrationAttempt Attempt,
    int? Win32ErrorCode = null);
