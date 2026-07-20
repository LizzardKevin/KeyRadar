namespace KeyRadar.Updater.Updates;

public enum UpdateManifestValidationError
{
    None,
    InvalidSignature,
    MalformedManifest,
    InvalidManifest,
    UntrustedDownload,
    AssetHashMismatch,
}
