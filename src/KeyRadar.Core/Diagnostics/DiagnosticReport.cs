namespace KeyRadar.Diagnostics;

public sealed record DiagnosticReport(
    string KeyRadarVersion,
    string OperatingSystem,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<DiagnosticApplication> Applications);

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
