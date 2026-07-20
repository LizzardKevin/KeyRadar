namespace KeyRadar.Windows.Foreground;

public static class ForegroundOverlayPolicy
{
    public static bool ShouldShow(int currentProcessId, int foregroundProcessId, bool hasAvailableRules) =>
        foregroundProcessId > 0 &&
        foregroundProcessId != currentProcessId &&
        hasAvailableRules;
}
