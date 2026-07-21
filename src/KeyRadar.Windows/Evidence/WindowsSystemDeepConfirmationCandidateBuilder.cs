namespace KeyRadar.Windows.Evidence;

public static class WindowsSystemHotkeyIdentity
{
    public const string ApplicationId = "windows-system";
    public const string GroupId = ApplicationId;

    public static bool IsSystemApplication(string applicationId) =>
        applicationId.Equals(ApplicationId, StringComparison.OrdinalIgnoreCase);
}
