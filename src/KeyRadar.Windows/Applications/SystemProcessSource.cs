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
                    var executableName = TryGetExecutableName(process);
                    descriptors.Add(new ProcessDescriptor(process.Id, process.ProcessName, executableName));
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the snapshot was being collected.
                }
            }
        }

        return descriptors;
    }

    private static string TryGetExecutableName(Process process)
    {
        try
        {
            return process.MainModule?.ModuleName ?? $"{process.ProcessName}.exe";
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return $"{process.ProcessName}.exe";
        }
    }
}
