using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyRadar.Diagnostics;

public static class DiagnosticBundleWriter
{
    private const int MaximumApplications = 2048;
    private const int MaximumHotkeysPerApplication = 2048;
    private const int MaximumProbeTelemetryItems = 2048;
    private static readonly HashSet<string> ProbeAvailabilityValues = new(StringComparer.Ordinal)
    {
        "Occupied", "AvailableAtScanTime", "SystemReserved", "ProbeError",
    };
    private static readonly HashSet<string> ProbeMechanismValues = new(StringComparer.Ordinal)
    {
        "RegisterHotKeyProbe",
    };
    private static readonly HashSet<string> ProbeOwnerStatusValues = new(StringComparer.Ordinal)
    {
        "Unknown",
    };
    private static readonly HashSet<string> DiagnosticStatusValues = new(StringComparer.Ordinal)
    {
        "Confirmed", "LocalConfigurationFound", "HardwareMappingFound", "WindowsKnown",
        "OfficialDefault", "PossibleOwner", "OccupiedOwnerUnknown", "Unknown",
    };
    private static readonly Regex AbsolutePathPattern = new(
        @"(?:[A-Za-z]:[\\/]|\\\\)[^\r\n]*",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static void Write(string outputPath, DiagnosticReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(report);
        if (report.Applications.Count > MaximumApplications ||
            report.Applications.Any(application => application.Hotkeys.Count > MaximumHotkeysPerApplication) ||
            report.ProbeTelemetry.Count > MaximumProbeTelemetryItems)
        {
            throw new InvalidDataException("The diagnostic snapshot exceeds its bounded record limits.");
        }

        var safeReport = report with
        {
            SchemaVersion = 2,
            KeyRadarVersion = Clean(report.KeyRadarVersion, 64),
            OperatingSystem = Clean(report.OperatingSystem, 160),
            Applications = report.Applications.Select(Sanitize).ToArray(),
            ProbeTelemetry = report.ProbeTelemetry
                .Select(Sanitize)
                .OrderBy(item => item.Gesture, StringComparer.Ordinal)
                .ThenBy(item => item.ScannedAtUtc)
                .ThenBy(item => item.Availability, StringComparer.Ordinal)
                .ThenBy(item => item.Mechanism, StringComparer.Ordinal)
                .ThenBy(item => item.Win32ErrorCode)
                .ToArray(),
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(safeReport, JsonOptions);
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var destination = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "README.txt",
            Encoding.UTF8.GetBytes(
                "KeyRadar diagnostics\n\nThis archive excludes usernames, full paths, window titles, ordinary key streams, and telemetry identifiers.\n"));
        WriteEntry(archive, "diagnostics.json", json);
    }

    private static DiagnosticApplication Sanitize(DiagnosticApplication application) =>
        application with
        {
            ApplicationId = Clean(application.ApplicationId, 100),
            DisplayName = Clean(application.DisplayName, 160),
            ExecutableName = SafeFileName(application.ExecutableName),
            Version = CleanOptional(application.Version, 100),
            Publisher = CleanOptional(application.Publisher, 160),
            Architecture = Clean(application.Architecture, 32),
            Privilege = Clean(application.Privilege, 32),
            Presence = Clean(application.Presence, 32),
            Hotkeys = application.Hotkeys.Select(hotkey => hotkey with
            {
                Gesture = Clean(hotkey.Gesture, 64),
                Function = Clean(hotkey.Function, 160),
                Scope = Clean(hotkey.Scope, 32),
                Confidence = Clean(hotkey.Confidence, 32),
                Evidence = Clean(hotkey.Evidence, 80),
            }).ToArray(),
        };

    private static DiagnosticProbeTelemetry Sanitize(DiagnosticProbeTelemetry probe) => probe with
    {
        Gesture = Clean(probe.Gesture, 64),
        Availability = SafeProbeValue(probe.Availability, ProbeAvailabilityValues, "Unknown"),
        Mechanism = SafeProbeValue(probe.Mechanism, ProbeMechanismValues, "Unknown"),
        OwnerStatus = SafeProbeValue(probe.OwnerStatus, ProbeOwnerStatusValues, "Unknown"),
        DiagnosticStatus = SafeProbeValue(probe.DiagnosticStatus, DiagnosticStatusValues, "Unknown"),
    };

    private static string SafeProbeValue(string? value, ISet<string> allowedValues, string fallback) =>
        allowedValues.Contains(value ?? string.Empty) ? value! : fallback;

    private static string SafeFileName(string value)
    {
        var normalized = value.Replace('\\', '/');
        return Clean(normalized[(normalized.LastIndexOf('/') + 1)..], 260);
    }

    private static string? CleanOptional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Clean(value, maximumLength);

    private static string Clean(string? value, int maximumLength)
    {
        var redacted = AbsolutePathPattern.Replace(value ?? string.Empty, "[path]");
        var userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            redacted = Regex.Replace(
                redacted,
                Regex.Escape(userName),
                "[user]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }

        var cleaned = new string(redacted
            .Where(character => !char.IsControl(character))
            .Take(maximumLength)
            .ToArray());
        return cleaned.Trim();
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
