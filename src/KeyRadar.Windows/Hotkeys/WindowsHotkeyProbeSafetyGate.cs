using System.Runtime.InteropServices;

namespace KeyRadar.Windows.Hotkeys;

public sealed partial class WindowsHotkeyProbeSafetyGate : IHotkeyProbeSafetyGate
{
    private const uint DesktopReadObjects = 0x0001;
    private const int UserObjectName = 2;
    private static readonly int[] KeyboardVirtualKeys =
    [
        0x08, 0x09, 0x0D, 0x10, 0x11, 0x12, 0x14, 0x1B,
        .. Enumerable.Range(0x20, 0x0F),
        .. Enumerable.Range(0x30, 0x0A),
        .. Enumerable.Range(0x41, 0x1A),
        0x5B, 0x5C,
        .. Enumerable.Range(0x60, 0x10),
        .. Enumerable.Range(0x70, 0x18),
        0x90, 0x91,
        .. Enumerable.Range(0xA0, 0x18),
        .. Enumerable.Range(0xBA, 0x07),
        .. Enumerable.Range(0xDB, 0x05),
    ];

    private readonly Func<int, short> _getKeyState;
    private readonly Func<bool> _isDefaultDesktop;

    public WindowsHotkeyProbeSafetyGate(
        Func<int, short>? getKeyState = null,
        Func<bool>? isDefaultDesktop = null)
    {
        _getKeyState = getKeyState ?? GetAsyncKeyState;
        _isDefaultDesktop = isDefaultDesktop ?? IsDefaultDesktop;
    }

    public HotkeyProbeSafety Check()
    {
        if (!_isDefaultDesktop())
        {
            return HotkeyProbeSafety.Cancel;
        }

        foreach (var virtualKey in KeyboardVirtualKeys)
        {
            if ((_getKeyState(virtualKey) & 0x8000) != 0)
            {
                return HotkeyProbeSafety.Pause;
            }
        }

        return HotkeyProbeSafety.Safe;
    }

    private static unsafe bool IsDefaultDesktop()
    {
        var desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == nint.Zero)
        {
            return false;
        }

        try
        {
            Span<char> name = stackalloc char[64];
            fixed (char* namePointer = name)
            {
                if (!GetUserObjectInformationW(
                        desktop,
                        UserObjectName,
                        namePointer,
                        (uint)(name.Length * sizeof(char)),
                        out _))
                {
                    return false;
                }
            }

            var terminator = name.IndexOf('\0');
            var desktopName = new string(name[..(terminator >= 0 ? terminator : name.Length)]);
            if (!desktopName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }

        return true;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(nint desktop);

    [LibraryImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserObjectInformationW(
        nint handle,
        int index,
        char* information,
        uint length,
        out uint requiredLength);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);
}
