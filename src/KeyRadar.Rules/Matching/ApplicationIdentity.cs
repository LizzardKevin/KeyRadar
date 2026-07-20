namespace KeyRadar.Rules;

public sealed record ApplicationIdentity(
    string ExecutableName,
    string? Version = null,
    string? Publisher = null,
    string? CompanyName = null,
    string? PackageFamilyName = null,
    string? Distribution = null);
