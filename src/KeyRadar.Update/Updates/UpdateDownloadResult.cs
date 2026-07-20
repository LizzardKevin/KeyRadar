namespace KeyRadar.Updater.Updates;

public sealed record UpdateDownloadResult(bool IsValid, string Message, string? FilePath)
{
    public static UpdateDownloadResult Success(string filePath) => new(true, string.Empty, filePath);

    public static UpdateDownloadResult Failure(string message) => new(false, message, null);
}
