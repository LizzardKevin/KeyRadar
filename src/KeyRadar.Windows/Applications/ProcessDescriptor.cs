namespace KeyRadar.Windows.Applications;

[Flags]
public enum ProcessMetadataUnavailable
{
    None = 0,
    ExecutableIdentity = 1,
    Version = 2,
    PublisherOrCompany = 4,
    Architecture = 8,
    PrivilegeLevel = 16,
    PackageOrDistribution = 32,
}

public sealed record ProcessDescriptor(
    int Id,
    string Name,
    string ExecutableName,
    string? Version = null,
    string? Publisher = null,
    ProcessArchitecture Architecture = ProcessArchitecture.Unknown,
    ProcessPrivilegeLevel PrivilegeLevel = ProcessPrivilegeLevel.Unknown,
    string? CompanyName = null,
    string? PackageFamilyName = null,
    string? Distribution = null,
    ProcessMetadataUnavailable UnavailableMetadata = ProcessMetadataUnavailable.None);
