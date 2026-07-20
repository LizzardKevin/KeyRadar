using System.Text.Json.Serialization;

namespace KeyRadar.Rules.Updates;

public sealed record RuleUpdateManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("packId")] string PackId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("assetName")] string AssetName,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("publishedAtUtc")] DateTimeOffset PublishedAtUtc);
