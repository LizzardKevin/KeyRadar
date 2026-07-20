using System.ComponentModel;
using System.Runtime.InteropServices;
using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.Input;

public sealed partial class GlobalHotkeyObserver : IDisposable
{
    private const int LowLevelKeyboardHook = 13;
    private const int KeyDownMessage = 0x0100;
    private const int KeyUpMessage = 0x0101;
    private const int SystemKeyDownMessage = 0x0104;
    private const int SystemKeyUpMessage = 0x0105;
    private const uint InjectedFlag = 0x10;

    private readonly HotkeyObservationState _state = new();
    private readonly HookProcedure _hookProcedure;
    private nint _hookHandle;

    public GlobalHotkeyObserver()
    {
        _hookProcedure = HookCallback;
    }

    public event EventHandler<HotkeyGestureObservedEventArgs>? GestureObserved;

    public bool IsRunning => _hookHandle != nint.Zero;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _hookHandle = SetWindowsHookEx(
            LowLevelKeyboardHook,
            _hookProcedure,
            GetModuleHandle(null),
            0);
        if (_hookHandle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to start passive hotkey observation.");
        }
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_hookHandle);
        _hookHandle = nint.Zero;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var keyboardEvent = Marshal.PtrToStructure<LowLevelKeyboardEvent>(data);
            if ((keyboardEvent.Flags & InjectedFlag) == 0)
            {
                var messageId = message.ToInt32();
                var isKeyDown = messageId is KeyDownMessage or SystemKeyDownMessage;
                var isKeyUp = messageId is KeyUpMessage or SystemKeyUpMessage;
                if (isKeyDown || isKeyUp)
                {
                    HotkeyGesture? gesture = _state.Process((int)keyboardEvent.VirtualKey, isKeyDown);
                    if (gesture is not null)
                    {
                        GestureObserved?.Invoke(this, new HotkeyGestureObservedEventArgs(gesture.Value));
                    }
                }
            }
        }

        // KeyRadar is observation-only: every event is always forwarded to its original target.
        return CallNextHookEx(_hookHandle, code, message, data);
    }

    private delegate nint HookProcedure(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LowLevelKeyboardEvent
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial nint SetWindowsHookEx(
        int hookId,
        HookProcedure callback,
        nint moduleHandle,
        uint threadId);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hookHandle, int code, nint message, nint data);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hookHandle);
}
