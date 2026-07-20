namespace KeyRadar.Windows.Applications;

public static class ApplicationPresenceClassifier
{
    public static IReadOnlyList<ApplicationSnapshot> Classify(
        IEnumerable<ProcessDescriptor> processes,
        IEnumerable<WindowDescriptor> windows,
        nint foregroundWindow)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(windows);

        var foregroundProcessId = foregroundWindow == nint.Zero
            ? null
            : windows
                .Where(window => window.Handle == foregroundWindow)
                .Select(window => (int?)window.ProcessId)
                .FirstOrDefault();

        return processes
            .Select(process => new ApplicationSnapshot(
                process,
                process.Id == foregroundProcessId
                    ? ApplicationPresence.Foreground
                    : ApplicationPresence.Background))
            .ToArray();
    }
}
