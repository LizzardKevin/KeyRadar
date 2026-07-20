namespace KeyRadar.Windows.Foreground;

public sealed record ForegroundApplication(int ProcessId, nint WindowHandle, string ExecutableName);
