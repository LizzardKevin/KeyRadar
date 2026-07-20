namespace KeyRadar.Rules.Updates;

public enum RuleUpdateStatus
{
    Failed,
    UpToDate,
    UpdateAvailable,
}

public sealed record RuleUpdateCheckResult(
    RuleUpdateStatus Status,
    string Message,
    RuleUpdateManifest? Manifest)
{
    public static RuleUpdateCheckResult Failure(string message) =>
        new(RuleUpdateStatus.Failed, message, null);

    public static RuleUpdateCheckResult Current(RuleUpdateManifest manifest) =>
        new(RuleUpdateStatus.UpToDate, $"Rule pack {manifest.Version} is already active.", manifest);

    public static RuleUpdateCheckResult Available(RuleUpdateManifest manifest) =>
        new(RuleUpdateStatus.UpdateAvailable, $"Rule pack {manifest.Version} is available.", manifest);
}

public sealed record RuleUpdateDownloadResult(bool IsSuccess, string Message, string? PackagePath)
{
    public static RuleUpdateDownloadResult Success(string path) =>
        new(true, "The signed rule pack was downloaded and verified.", path);

    public static RuleUpdateDownloadResult Failure(string message) =>
        new(false, message, null);
}
