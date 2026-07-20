namespace KeyRadar.Windows.Applications;

public interface IProcessSource
{
    IReadOnlyList<ProcessDescriptor> ReadProcesses();
}
