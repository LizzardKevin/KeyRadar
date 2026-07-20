namespace KeyRadar.Windows.Applications;

public sealed class RunningApplicationScanner(IProcessSource processSource, IWindowSource windowSource)
{
    public IReadOnlyList<ApplicationSnapshot> Scan(int currentProcessId)
    {
        var processes = processSource
            .ReadProcesses()
            .Where(process => process.Id != currentProcessId);

        return ApplicationPresenceClassifier.Classify(
            processes,
            windowSource.ReadWindows(),
            windowSource.GetForegroundWindow());
    }
}
