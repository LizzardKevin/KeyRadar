using System.Runtime.InteropServices;

namespace KeyRadar.Windows.Applications;

public sealed partial class Win32WindowSource : IWindowSource
{
    public IReadOnlyList<WindowDescriptor> ReadWindows()
    {
        var windows = new List<WindowDescriptor>();

        _ = EnumWindows((handle, _) =>
        {
            if (IsWindowVisible(handle) &&
                GetWindowThreadProcessId(handle, out var processId) != 0 &&
                processId <= int.MaxValue)
            {
                windows.Add(new WindowDescriptor(handle, (int)processId));
            }

            return true;
        }, nint.Zero);

        return windows;
    }

    nint IWindowSource.GetForegroundWindow() => GetForegroundWindow();

    private delegate bool EnumWindowsProc(nint windowHandle, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint windowHandle);
}
