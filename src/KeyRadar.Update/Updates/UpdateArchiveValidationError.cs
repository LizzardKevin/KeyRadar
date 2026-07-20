namespace KeyRadar.Updater.Updates;

public enum UpdateArchiveValidationError
{
    None,
    MalformedArchive,
    UnsafeEntry,
    PackageTooLarge,
    MissingApplication,
    DestinationNotEmpty,
}
