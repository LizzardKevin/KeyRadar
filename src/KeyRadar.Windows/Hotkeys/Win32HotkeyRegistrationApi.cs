using System.Runtime.InteropServices;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.DeepConfirmation;

namespace KeyRadar.Windows.Hotkeys;

public sealed partial class Win32HotkeyRegistrationApi : IHotkeyRegistrationApi, IDisposable
{
    private const int HotkeyAlreadyRegisteredError = 1409;
    private const int InvalidHotkeyError = 1422;
    private const uint NoRepeatModifier = 0x4000;
    private static readonly nint MessageOnlyWindow = new(-3);
    private readonly Dictionary<int, nint> _registeredWindows = [];
    private readonly object _gate = new();

    public HotkeyRegistrationResult TryRegister(int identifier, HotkeyGesture gesture)
    {
        if (!NativeHotkeyMapper.TryMap(gesture, out var virtualKey, out var modifiers))
        {
            return new HotkeyRegistrationResult(HotkeyRegistrationAttempt.SystemReserved);
        }

        var window = CreateWindowExW(
            0,
            "STATIC",
            "KeyRadar.HotkeyProbe",
            0,
            0,
            0,
            0,
            0,
            MessageOnlyWindow,
            nint.Zero,
            nint.Zero,
            nint.Zero);
        if (window == nint.Zero)
        {
            return new HotkeyRegistrationResult(
                HotkeyRegistrationAttempt.Error,
                Marshal.GetLastWin32Error());
        }

        if (RegisterHotKey(window, identifier, modifiers | NoRepeatModifier, virtualKey))
        {
            lock (_gate)
            {
                _registeredWindows.Add(identifier, window);
            }

            return new HotkeyRegistrationResult(HotkeyRegistrationAttempt.Registered);
        }

        var error = Marshal.GetLastWin32Error();
        _ = DestroyWindow(window);
        return error switch
        {
            HotkeyAlreadyRegisteredError => new(
                HotkeyRegistrationAttempt.AlreadyRegistered,
                error),
            InvalidHotkeyError => new(HotkeyRegistrationAttempt.SystemReserved, error),
            _ => new(HotkeyRegistrationAttempt.Error, error),
        };
    }

    public void Unregister(int identifier)
    {
        nint window;
        lock (_gate)
        {
            if (!_registeredWindows.Remove(identifier, out window))
            {
                return;
            }
        }

        try
        {
            _ = UnregisterHotKey(window, identifier);
        }
        finally
        {
            _ = DestroyWindow(window);
        }
    }

    public void Dispose()
    {
        int[] identifiers;
        lock (_gate)
        {
            identifiers = [.. _registeredWindows.Keys];
        }

        foreach (var identifier in identifiers)
        {
            Unregister(identifier);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint windowHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint windowHandle, int identifier, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint windowHandle, int identifier);
}
