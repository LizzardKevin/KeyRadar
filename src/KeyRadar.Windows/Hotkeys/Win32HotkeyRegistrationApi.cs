using System.Runtime.InteropServices;
using KeyRadar.Shortcuts;

namespace KeyRadar.Windows.Hotkeys;

public sealed partial class Win32HotkeyRegistrationApi : IHotkeyRegistrationApi
{
    private const int HotkeyAlreadyRegisteredError = 1409;
    private const uint NoRepeatModifier = 0x4000;

    public HotkeyRegistrationAttempt TryRegister(int identifier, ShortcutGesture gesture)
    {
        if (!TryMapVirtualKey(gesture.Key, out var virtualKey))
        {
            return HotkeyRegistrationAttempt.Unsupported;
        }

        var modifiers = MapModifiers(gesture.Modifiers) | NoRepeatModifier;
        if (RegisterHotKey(nint.Zero, identifier, modifiers, virtualKey))
        {
            return HotkeyRegistrationAttempt.Registered;
        }

        return Marshal.GetLastWin32Error() == HotkeyAlreadyRegisteredError
            ? HotkeyRegistrationAttempt.AlreadyRegistered
            : HotkeyRegistrationAttempt.Unsupported;
    }

    public void Unregister(int identifier) => _ = UnregisterHotKey(nint.Zero, identifier);

    private static uint MapModifiers(ShortcutModifiers modifiers)
    {
        var nativeModifiers = 0u;
        if (modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            nativeModifiers |= 0x0001;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Control))
        {
            nativeModifiers |= 0x0002;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            nativeModifiers |= 0x0004;
        }

        if (modifiers.HasFlag(ShortcutModifiers.Windows))
        {
            nativeModifiers |= 0x0008;
        }

        return nativeModifiers;
    }

    private static bool TryMapVirtualKey(string key, out uint virtualKey)
    {
        if (key.Length == 1)
        {
            var character = char.ToUpperInvariant(key[0]);
            if (character is >= '0' and <= '9' or >= 'A' and <= 'Z')
            {
                virtualKey = character;
                return true;
            }
        }

        if (key.StartsWith('F') &&
            int.TryParse(key.AsSpan(1), out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x6F + functionKey);
            return true;
        }

        virtualKey = key switch
        {
            "Backspace" => 0x08,
            "Tab" => 0x09,
            "Enter" => 0x0D,
            "Esc" => 0x1B,
            "Space" => 0x20,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "End" => 0x23,
            "Home" => 0x24,
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            "PrintScreen" => 0x2C,
            "Insert" => 0x2D,
            "Delete" => 0x2E,
            _ => 0,
        };
        return virtualKey != 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint windowHandle, int identifier, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint windowHandle, int identifier);
}
