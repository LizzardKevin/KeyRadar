namespace KeyRadar.Rules.Packs;

public enum RulePackValidationError
{
    None,
    MalformedArchive,
    MissingManifest,
    DisallowedEntry,
    PackageTooLarge,
    InvalidManifest,
    MissingFile,
    UnexpectedFile,
    HashMismatch,
    InvalidSignature,
}
