using System.Threading;
using KeyRadar.Diagnostics;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Evidence;

/// <summary>
/// The immutable export inputs produced by one completed scan.
/// </summary>
public sealed class CompletedScanExportSnapshot
{
    private CompletedScanExportSnapshot(
        IReadOnlyList<DiagnosticApplication> applications,
        IReadOnlyList<HotkeyProbeResult> probeResults,
        IReadOnlyList<DiscoveredHotkey> diagnosticItems)
    {
        Applications = applications;
        ProbeResults = probeResults;
        DiagnosticItems = diagnosticItems;
    }

    public IReadOnlyList<DiagnosticApplication> Applications { get; }

    public IReadOnlyList<HotkeyProbeResult> ProbeResults { get; }

    public IReadOnlyList<DiscoveredHotkey> DiagnosticItems { get; }

    public static CompletedScanExportSnapshot Empty { get; } = Create([], [], []);

    public static CompletedScanExportSnapshot Create(
        IEnumerable<DiagnosticApplication> applications,
        IEnumerable<HotkeyProbeResult> probeResults,
        IEnumerable<DiscoveredHotkey> diagnosticItems)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(probeResults);
        ArgumentNullException.ThrowIfNull(diagnosticItems);

        return new CompletedScanExportSnapshot(
            Freeze(applications.Select(application => application with { Hotkeys = Freeze(application.Hotkeys) })),
            Freeze(probeResults),
            Freeze(diagnosticItems
                .Select(item => item with
                {
                    Owners = Freeze(item.Owners),
                    Evidence = Freeze(item.Evidence),
                    EvidenceOwnerIdentities = item.EvidenceOwnerIdentities is null
                        ? null
                        : Freeze(item.EvidenceOwnerIdentities),
                })
                .ToArray()));
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> items) =>
        Array.AsReadOnly(items.ToArray());
}

/// <summary>
/// Atomically exposes only the most recently completed export snapshot.
/// </summary>
public sealed class CompletedScanStateStore
{
    private CompletedScanExportSnapshot _current;

    public CompletedScanStateStore(CompletedScanExportSnapshot? initial = null) =>
        _current = initial ?? CompletedScanExportSnapshot.Empty;

    public CompletedScanExportSnapshot Current => Volatile.Read(ref _current);

    public void Publish(CompletedScanExportSnapshot completedScan) =>
        Interlocked.Exchange(ref _current, completedScan ?? throw new ArgumentNullException(nameof(completedScan)));
}
