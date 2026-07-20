namespace KeyRadar.Rules;

public sealed record ApplicationMatchRule(
    IReadOnlyList<string> Executables,
    IReadOnlyList<string> Publishers,
    VersionRange? VersionRange,
    IReadOnlyList<string> PackageFamilyNames,
    string? Distribution);
