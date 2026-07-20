namespace KeyRadar.Windows.Applications;

public sealed record ProcessDescriptor(
    int Id,
    string Name,
    string ExecutableName,
    string? Version = null,
    string? Publisher = null,
    ProcessArchitecture Architecture = ProcessArchitecture.Unknown,
    ProcessPrivilegeLevel PrivilegeLevel = ProcessPrivilegeLevel.Unknown);
