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

}
