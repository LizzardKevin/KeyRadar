namespace KeyRadar.Updater.Updates;

public sealed record UpdateArchiveExtractionResult(
    bool IsValid,
    UpdateArchiveValidationError Error,
    string Message)
{
    public static UpdateArchiveExtractionResult Success() =>
        new(true, UpdateArchiveValidationError.None, string.Empty);

    public static UpdateArchiveExtractionResult Failure(UpdateArchiveValidationError error, string message) =>
        new(false, error, message);
}
