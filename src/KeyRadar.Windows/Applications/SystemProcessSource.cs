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

    public static IReadOnlyList<ProcessDescriptor> ReadCurrentSessionProcesses(int currentProcessId)
    {
        using var current = Process.GetCurrentProcess();
        var sessionId = current.SessionId;
        var descriptors = new List<ProcessDescriptor>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != currentProcessId && process.SessionId == sessionId)
                    {
                        descriptors.Add(ProcessMetadataReader.Read(process));
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process exited while the elevated snapshot was being collected.
                }
            }
        }

        return descriptors;
    }

}
