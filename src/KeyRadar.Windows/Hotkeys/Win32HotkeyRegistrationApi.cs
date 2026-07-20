using System.Runtime.InteropServices;
using KeyRadar.Shortcuts;
using KeyRadar.Windows.DeepConfirmation;

namespace KeyRadar.Windows.Hotkeys;

public sealed partial class Win32HotkeyRegistrationApi : IHotkeyRegistrationApi
{
    private const int HotkeyAlreadyRegisteredError = 1409;
    private const uint NoRepeatModifier = 0x4000;

    public HotkeyRegistrationAttempt TryRegister(int identifier, ShortcutGesture gesture)
    {
        if (!NativeHotkeyMapper.TryMap(gesture, out var virtualKey, out var modifiers))
        {
            return HotkeyRegistrationAttempt.Unsupported;
        }

        if (RegisterHotKey(nint.Zero, identifier, modifiers | NoRepeatModifier, virtualKey))
        {
            return HotkeyRegistrationAttempt.Registered;
        }

        return Marshal.GetLastWin32Error() == HotkeyAlreadyRegisteredError
            ? HotkeyRegistrationAttempt.AlreadyRegistered
            : HotkeyRegistrationAttempt.Unsupported;
    }

    public void Unregister(int identifier) => _ = UnregisterHotKey(nint.Zero, identifier);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint windowHandle, int identifier, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint windowHandle, int identifier);
}
