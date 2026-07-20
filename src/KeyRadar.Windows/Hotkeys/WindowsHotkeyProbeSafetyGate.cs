using System.Runtime.InteropServices;

namespace KeyRadar.Windows.Hotkeys;

public sealed partial class WindowsHotkeyProbeSafetyGate : IHotkeyProbeSafetyGate
{
    private const uint DesktopReadObjects = 0x0001;
    private const int UserObjectName = 2;

    public unsafe HotkeyProbeSafety Check()
    {
        var desktop = OpenInputDesktop(0, false, DesktopReadObjects);
        if (desktop == nint.Zero)
        {
            return HotkeyProbeSafety.Cancel;
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
                    return HotkeyProbeSafety.Cancel;
                }
            }

            var terminator = name.IndexOf('\0');
            var desktopName = new string(name[..(terminator >= 0 ? terminator : name.Length)]);
            if (!desktopName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                return HotkeyProbeSafety.Cancel;
            }
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }

        for (var virtualKey = 1; virtualKey < 255; virtualKey++)
        {
            if ((GetAsyncKeyState(virtualKey) & 0x8000) != 0)
            {
                return HotkeyProbeSafety.Pause;
            }
        }

        return HotkeyProbeSafety.Safe;
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
