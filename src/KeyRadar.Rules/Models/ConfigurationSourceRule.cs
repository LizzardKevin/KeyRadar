using KeyRadar.Conflicts;

namespace KeyRadar.Rules;

public enum ConfigurationSourceRoot
{
    LocalAppData,
    RoamingAppData,
    Documents,
    ProgramData,
}

public enum ConfigurationSourceFormat
{
    Json,
    Ini,
}

public enum ConfigurationGestureDecoder
{
    GestureString,
    VirtualKeyArray,
    WinFormsHotkey,
}

/// <summary>Bounded, declarative extraction instructions for one application configuration file.</summary>
public sealed record ConfigurationSourceRule(
    string SourceId,
    ConfigurationSourceRoot Root,
    string RelativePath,
    ConfigurationSourceFormat Format,
    int MaxBytes,
    IReadOnlyList<ConfigurationEntryRule> Entries);

public sealed record ConfigurationEntryRule(
    string CommandId,
    string GestureSelector,
    ConfigurationGestureDecoder Decoder,
    LocalizedText Function,
    HotkeyScope Scope)
{
    public string? CollectionSelector { get; init; }
    public string? WinSelector { get; init; }
    public string? FunctionSelector { get; init; }
    public IReadOnlyDictionary<string, LocalizedText> FunctionValues { get; init; } =
        new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> CommandIdValues { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
