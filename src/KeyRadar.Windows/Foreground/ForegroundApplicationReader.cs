using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyRadar.Windows.Foreground;

public static partial class ForegroundApplicationReader
{
    private static readonly Lock CacheLock = new();
    private static nint _cachedWindowHandle;
    private static uint _cachedProcessId;
    private static string? _cachedExecutableName;

    public static ForegroundApplication? Read()
    {
        var windowHandle = GetForegroundWindow();
        if (windowHandle == nint.Zero || GetWindowThreadProcessId(windowHandle, out var processId) == 0)
        {
            return null;
        }

        lock (CacheLock)
        {
            if (_cachedWindowHandle == windowHandle &&
                _cachedProcessId == processId &&
                _cachedExecutableName is not null)
            {
                return new ForegroundApplication((int)processId, windowHandle, _cachedExecutableName);
            }
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var executableName = TryGetExecutableName(process);
            lock (CacheLock)
            {
                _cachedWindowHandle = windowHandle;
                _cachedProcessId = processId;
                _cachedExecutableName = executableName;
            }

            return new ForegroundApplication(process.Id, windowHandle, executableName);
        }
        catch (ArgumentException)
        {
            return null;
        }
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

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}
