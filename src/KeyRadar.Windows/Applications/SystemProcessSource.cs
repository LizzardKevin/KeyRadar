using System.Diagnostics;

namespace KeyRadar.Windows.Applications;

public sealed class SystemProcessSource : IProcessSource
{
    public IReadOnlyList<ProcessDescriptor> ReadProcesses()
    {
        var descriptors = new List<ProcessDescriptor>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    descriptors.Add(ProcessMetadataReader.Read(process));
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the snapshot was being collected.
                }
            }
        }

        return descriptors;
    }

    public static IReadOnlyList<ProcessDescriptor> ReadAllowedProcesses(
        int currentProcessId,
        IReadOnlyList<ElevatedProcessTarget> allowlist,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowlist);
        cancellationToken.ThrowIfCancellationRequested();
        using var current = Process.GetCurrentProcess();
        var sessionId = current.SessionId;
        var descriptors = new List<ProcessDescriptor>();
        foreach (var target in allowlist)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target.Id <= 0 || target.Id == currentProcessId)
            {
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(target.Id);
                if (process.SessionId != sessionId || !HasExpectedStartTime(process, target.StartTimeUtcTicks) ||
                    IsKeyRadarInfrastructure(process.ProcessName))
                {
                    continue;
                }

                descriptors.Add(ProcessMetadataReader.Read(process));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The allowlisted process exited or changed identity before it could be read.
            }
        }

        return descriptors;
    }

    private static bool HasExpectedStartTime(Process process, long? expectedStartTimeUtcTicks) =>
        expectedStartTimeUtcTicks is null || process.StartTime.ToUniversalTime().Ticks == expectedStartTimeUtcTicks;

    private static bool IsKeyRadarInfrastructure(string processName) =>
        string.Equals(processName, "KeyRadar", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "KeyRadar.ElevatedScanner", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "KeyRadar.Updater", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "KeyRadar.NativeHost", StringComparison.OrdinalIgnoreCase) ||
        processName.StartsWith("KeyRadar.NativeHost.", StringComparison.OrdinalIgnoreCase);

}
