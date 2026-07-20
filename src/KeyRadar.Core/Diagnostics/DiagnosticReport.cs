using System.Text.Json.Serialization;

namespace KeyRadar.Diagnostics;

public sealed record DiagnosticReport(
    string KeyRadarVersion,
    string OperatingSystem,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<DiagnosticApplication> Applications)
{
    // Version 2 adds privacy-bounded RegisterHotKey probe telemetry. The
    // original positional constructor remains unchanged for source compatibility.
    public int SchemaVersion { get; init; } = 2;

    public IReadOnlyList<DiagnosticProbeTelemetry> ProbeTelemetry { get; init; } = [];
}

public sealed record DiagnosticApplication(
    string ApplicationId,
    string DisplayName,
    string ExecutableName,
    string? Version,
    string? Publisher,
    string Architecture,
    string Privilege,
    string Presence,
    IReadOnlyList<DiagnosticHotkey> Hotkeys);

public sealed record DiagnosticHotkey(
    string Gesture,
    string Function,
    string Scope,
    string Confidence,
    string Evidence);

public sealed record DiagnosticProbeTelemetry(
    string Gesture,
    string Availability,
    string Mechanism,
    DateTimeOffset ScannedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Win32ErrorCode,
    string OwnerStatus,
    string DiagnosticStatus);
