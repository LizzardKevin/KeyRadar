namespace KeyRadar.Updater.Updates;

public sealed record UpdateManifestValidationResult(
    bool IsValid,
    UpdateManifestValidationError Error,
    string Message,
    UpdateManifest? Manifest)
{
    public static UpdateManifestValidationResult Success(UpdateManifest manifest) =>
        new(true, UpdateManifestValidationError.None, string.Empty, manifest);

    public static UpdateManifestValidationResult Failure(UpdateManifestValidationError error, string message) =>
        new(false, error, message, null);
}
