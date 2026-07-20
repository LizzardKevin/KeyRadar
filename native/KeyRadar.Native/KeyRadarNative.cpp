#define WIN32_LEAN_AND_MEAN
#include <windows.h>

namespace
{
    constexpr DWORD ComponentAbiVersion = 1;
    constexpr int ProbeIdentifier = 0x4B52;
    constexpr DWORD HotkeyAlreadyRegisteredError = 1409;
    constexpr DWORD InvalidHotkeyError = 1422;

    enum class ProbeStatus : DWORD
    {
        AvailableAtProbeTime = 0,
        Occupied = 1,
        SystemReserved = 2,
        ProbeError = 3,
    };
}

extern "C" __declspec(dllexport) DWORD GetComponentAbiVersion()
{
    return ComponentAbiVersion;
}

extern "C" __declspec(dllexport) DWORD ProbeHotkey(
    UINT virtualKey,
    UINT modifiers,
    DWORD* win32ErrorCode)
{
    if (win32ErrorCode != nullptr)
    {
        *win32ErrorCode = ERROR_SUCCESS;
    }

    if (virtualKey == 0 || (modifiers & ~(MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_WIN)) != 0)
    {
        if (win32ErrorCode != nullptr)
        {
            *win32ErrorCode = ERROR_INVALID_PARAMETER;
        }
        return static_cast<DWORD>(ProbeStatus::ProbeError);
    }

    const auto window = CreateWindowExW(
        0,
        L"STATIC",
        L"KeyRadar.Native.HotkeyProbe",
        0,
        0,
        0,
        0,
        0,
        HWND_MESSAGE,
        nullptr,
        GetModuleHandleW(nullptr),
        nullptr);
    if (window == nullptr)
    {
        if (win32ErrorCode != nullptr)
        {
            *win32ErrorCode = GetLastError();
        }
        return static_cast<DWORD>(ProbeStatus::ProbeError);
    }

    if (RegisterHotKey(window, ProbeIdentifier, modifiers | MOD_NOREPEAT, virtualKey))
    {
        UnregisterHotKey(window, ProbeIdentifier);
        DestroyWindow(window);
        return static_cast<DWORD>(ProbeStatus::AvailableAtProbeTime);
    }

    const auto error = GetLastError();
    DestroyWindow(window);
    if (win32ErrorCode != nullptr)
    {
        *win32ErrorCode = error;
    }

    if (error == HotkeyAlreadyRegisteredError)
    {
        return static_cast<DWORD>(ProbeStatus::Occupied);
    }
    if (error == InvalidHotkeyError)
    {
        return static_cast<DWORD>(ProbeStatus::SystemReserved);
    }
    return static_cast<DWORD>(ProbeStatus::ProbeError);
}
