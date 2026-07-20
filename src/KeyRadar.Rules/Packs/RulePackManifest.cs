using System.Text.Json.Serialization;

namespace KeyRadar.Rules.Packs;

internal sealed record RulePackManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("packId")] string PackId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("files")] IReadOnlyList<RulePackManifestFile> Files,
    [property: JsonPropertyName("source")] string? Source = null);

internal sealed record RulePackManifestFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256);
