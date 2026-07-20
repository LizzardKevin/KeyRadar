namespace KeyRadar.Updater.Updates;

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string Message,
    UpdateManifest? Manifest)
{
    public static UpdateCheckResult Available(UpdateManifest manifest) =>
        new(UpdateCheckStatus.UpdateAvailable, $"KeyRadar {manifest.Version} is available.", manifest);

    public static UpdateCheckResult Current(UpdateManifest manifest) =>
        new(UpdateCheckStatus.UpToDate, "KeyRadar is up to date.", manifest);

    public static UpdateCheckResult Failure(string message) =>
        new(UpdateCheckStatus.Failed, message, null);
}
