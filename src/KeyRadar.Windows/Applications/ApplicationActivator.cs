using System.Runtime.InteropServices;

namespace KeyRadar.Windows.Applications;

public static partial class ApplicationActivator
{
    private const int RestoreWindow = 9;

    public static bool TryActivate(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        nint targetWindow = nint.Zero;

        _ = EnumWindows((windowHandle, _) =>
        {
            if (IsWindowVisible(windowHandle) &&
                GetWindowThreadProcessId(windowHandle, out var ownerProcessId) != 0 &&
                ownerProcessId == processId)
            {
                targetWindow = windowHandle;
                return false;
            }

            return true;
        }, nint.Zero);

        if (targetWindow == nint.Zero)
        {
            return false;
        }

        _ = ShowWindowAsync(targetWindow, RestoreWindow);
        return SetForegroundWindow(targetWindow);
    }

    private delegate bool EnumWindowsProc(nint windowHandle, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowAsync(nint windowHandle, int command);
}
