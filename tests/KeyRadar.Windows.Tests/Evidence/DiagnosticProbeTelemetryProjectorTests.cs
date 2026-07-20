using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class DiagnosticProbeTelemetryProjectorTests
{
    [Fact]
    public void Suppressed_occupied_bare_function_key_is_exported_with_unknown_owner_status()
    {
        var scannedAt = DateTimeOffset.Parse("2026-07-21T01:02:03Z", System.Globalization.CultureInfo.InvariantCulture);
        var f12 = HotkeyGesture.Parse("F12");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(f12, HotkeyProbeAvailability.Occupied, scannedAt, 1409)],
            [],
            [],
            []);

        Assert.Empty(catalog.Items);
        Assert.Empty(catalog.ActionableUnknownProbes);

        var telemetry = Assert.Single(DiagnosticProbeTelemetryProjector.Project(
            [Probe(f12, HotkeyProbeAvailability.Occupied, scannedAt, 1409)],
            catalog.DiagnosticItems));

        Assert.Equal("F12", telemetry.Gesture);
        Assert.Equal("Occupied", telemetry.Availability);
        Assert.Equal("RegisterHotKeyProbe", telemetry.Mechanism);
        Assert.Equal(scannedAt, telemetry.ScannedAtUtc);
        Assert.Equal(1409, telemetry.Win32ErrorCode);
        Assert.Equal("Unknown", telemetry.OwnerStatus);
        Assert.Equal("OccupiedOwnerUnknown", telemetry.DiagnosticStatus);
    }

    [Fact]
    public void Available_browser_key_remains_diagnostic_telemetry_without_becoming_inventory()
    {
        var scannedAt = DateTimeOffset.Parse("2026-07-21T02:03:04Z", System.Globalization.CultureInfo.InvariantCulture);
        var browserBack = HotkeyGesture.Parse("BrowserBack");
        HotkeyProbeResult[] probes = [Probe(browserBack, HotkeyProbeAvailability.AvailableAtScanTime, scannedAt)];
        var catalog = HotkeyAttributionCatalog.Create(probes, [], [], []);

        Assert.Empty(catalog.Items);

        var telemetry = Assert.Single(DiagnosticProbeTelemetryProjector.Project(probes, catalog.DiagnosticItems));

        Assert.Equal("BrowserBack", telemetry.Gesture);
        Assert.Equal("AvailableAtScanTime", telemetry.Availability);
        Assert.Equal("Unknown", telemetry.OwnerStatus);
        Assert.Equal("Unknown", telemetry.DiagnosticStatus);
    }

    [Fact]
    public void Projector_uses_one_latest_probe_per_gesture_in_deterministic_order_and_keeps_known_status()
    {
        var earlier = DateTimeOffset.Parse("2026-07-21T03:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var latest = earlier.AddMinutes(1);
        var altA = HotkeyGesture.Parse("Alt+A");
        var ctrlF12 = HotkeyGesture.Parse("Ctrl+F12");
        var probes = new[]
        {
            Probe(ctrlF12, HotkeyProbeAvailability.AvailableAtScanTime, earlier),
            Probe(altA, HotkeyProbeAvailability.Occupied, latest),
            Probe(ctrlF12, HotkeyProbeAvailability.Occupied, latest),
            Probe(altA, HotkeyProbeAvailability.AvailableAtScanTime, earlier),
        };
        var catalog = HotkeyAttributionCatalog.Create(
            probes,
            [new RunningRuleHotkey(
                "sample-app",
                ctrlF12,
                "Supported evidence only",
                HotkeyScope.Global,
                OwnershipConfidence.OfficialDefault,
                "official rule")],
            [],
            []);

        var telemetry = DiagnosticProbeTelemetryProjector.Project(probes.Reverse(), catalog.DiagnosticItems);

        Assert.Equal(["Alt+A", "Ctrl+F12"], telemetry.Select(item => item.Gesture));
        var known = Assert.Single(telemetry, item => item.Gesture == "Ctrl+F12");
        Assert.Equal(latest, known.ScannedAtUtc);
        Assert.Equal("Occupied", known.Availability);
        Assert.Equal("OfficialDefault", known.DiagnosticStatus);
        Assert.Equal(2, telemetry.Count);
    }

    private static HotkeyProbeResult Probe(
        HotkeyGesture gesture,
        HotkeyProbeAvailability availability,
        DateTimeOffset scannedAt,
        int? win32ErrorCode = null) => new(
        gesture,
        availability,
        HotkeyProbeMechanism.RegisterHotKeyProbe,
        HotkeyOwner.Unknown,
        scannedAt,
        win32ErrorCode);
}
