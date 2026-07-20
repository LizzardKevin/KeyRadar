namespace KeyRadar.Windows.Applications;

public interface IWindowSource
{
    IReadOnlyList<WindowDescriptor> ReadWindows();

    nint GetForegroundWindow();
}
