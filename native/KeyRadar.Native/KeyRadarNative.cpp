#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>

namespace
{
    constexpr DWORD ComponentAbiVersion = 1;
    constexpr int ProbeIdentifier = 0x4B52;
    constexpr DWORD HotkeyAlreadyRegisteredError = 1409;
    constexpr DWORD InvalidHotkeyError = 1422;
    constexpr DWORD OwnerTraceMagic = 0x4B525452; // "KRTR"
    constexpr DWORD OwnerTraceVersion = 2;
    constexpr wchar_t OwnerTraceMappingPrefix[] = L"Local\\KeyRadar.OwnerTrace.State";
    constexpr wchar_t OwnerTraceLocatorPrefix[] = L"Local\\KeyRadar.OwnerTrace.Locator";

    enum class ProbeStatus : DWORD { AvailableAtProbeTime = 0, Occupied = 1, SystemReserved = 2, ProbeError = 3 };

    struct OwnerTraceLocator
    {
        DWORD magic;
        DWORD version;
        DWORD ownerHostProcessId;
        ULONGLONG ownerHostStartTime;
        DWORD virtualKey;
        DWORD modifiers;
        BYTE nonce[16];
    };

    struct OwnerTraceState
    {
        DWORD magic;
        DWORD version;
        DWORD ownerHostProcessId;
        ULONGLONG ownerHostStartTime;
        DWORD virtualKey;
        DWORD modifiers;
        BYTE nonce[16];
        volatile LONG matched;
        volatile LONG processId;
        volatile LONG threadId;
    };

    bool IsProcessStillRunning(DWORD processId, ULONGLONG expectedStartTime)
    {
        const auto process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId);
        if (process == nullptr) return false;
        FILETIME created{}, ignored{};
        const auto succeeded = GetProcessTimes(process, &created, &ignored, &ignored, &ignored);
        CloseHandle(process);
        ULARGE_INTEGER start{};
        start.LowPart = created.dwLowDateTime;
        start.HighPart = created.dwHighDateTime;
        return succeeded && start.QuadPart == expectedStartTime;
    }

    void FormatNonce(const BYTE nonce[16], wchar_t (&text)[33])
    {
        for (size_t index = 0; index < 16; ++index) swprintf_s(text + index * 2, 3, L"%02x", nonce[index]);
    }

    bool BuildSessionName(const wchar_t* prefix, const BYTE nonce[16], bool includeNonce, wchar_t (&name)[192])
    {
        DWORD sessionId = 0;
        if (!ProcessIdToSessionId(GetCurrentProcessId(), &sessionId)) return false;
        if (!includeNonce) return swprintf_s(name, L"%s.%lu", prefix, sessionId) > 0;
        wchar_t nonceText[33]{};
        FormatNonce(nonce, nonceText);
        return swprintf_s(name, L"%s.%lu.%s", prefix, sessionId, nonceText) > 0;
    }

    bool IsExpected(const OwnerTraceState& state, const OwnerTraceLocator& locator, UINT virtualKey, UINT modifiers)
    {
        return locator.magic == OwnerTraceMagic && locator.version == OwnerTraceVersion &&
            locator.virtualKey == virtualKey && locator.modifiers == modifiers &&
            locator.ownerHostProcessId != 0 && locator.ownerHostStartTime != 0 &&
            IsProcessStillRunning(locator.ownerHostProcessId, locator.ownerHostStartTime) &&
            state.magic == OwnerTraceMagic && state.version == OwnerTraceVersion &&
            state.ownerHostProcessId == locator.ownerHostProcessId &&
            state.ownerHostStartTime == locator.ownerHostStartTime &&
            state.virtualKey == virtualKey && state.modifiers == modifiers &&
            memcmp(state.nonce, locator.nonce, sizeof(state.nonce)) == 0;
    }

    bool IsExpectedOwnerTraceMessage(const MSG& message)
    {
        if (message.message != WM_HOTKEY) return false;
        const auto modifiers = static_cast<UINT>(LOWORD(message.lParam));
        const auto virtualKey = static_cast<UINT>(HIWORD(message.lParam));
        wchar_t locatorName[192]{};
        if (!BuildSessionName(OwnerTraceLocatorPrefix, nullptr, false, locatorName)) return false;
        const auto locatorMapping = OpenFileMappingW(FILE_MAP_READ, FALSE, locatorName);
        if (locatorMapping == nullptr) return false;
        const auto locator = static_cast<OwnerTraceLocator*>(MapViewOfFile(locatorMapping, FILE_MAP_READ, 0, 0, sizeof(OwnerTraceLocator)));
        if (locator == nullptr) { CloseHandle(locatorMapping); return false; }
        OwnerTraceLocator copy{};
        memcpy(&copy, locator, sizeof(copy));
        UnmapViewOfFile(locator);
        CloseHandle(locatorMapping);
        if (copy.magic != OwnerTraceMagic || copy.version != OwnerTraceVersion ||
            copy.virtualKey != virtualKey || copy.modifiers != modifiers ||
            !IsProcessStillRunning(copy.ownerHostProcessId, copy.ownerHostStartTime)) return false;
        wchar_t stateName[192]{};
        if (!BuildSessionName(OwnerTraceMappingPrefix, copy.nonce, true, stateName)) return false;
        const auto stateMapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, stateName);
        if (stateMapping == nullptr) return false;
        const auto state = static_cast<OwnerTraceState*>(MapViewOfFile(stateMapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(OwnerTraceState)));
        if (state == nullptr) { CloseHandle(stateMapping); return false; }
        const auto expected = IsExpected(*state, copy, virtualKey, modifiers);
        if (expected && InterlockedCompareExchange(&state->processId, static_cast<LONG>(GetCurrentProcessId()), 0) == 0)
        {
            InterlockedExchange(&state->threadId, static_cast<LONG>(GetCurrentThreadId()));
            InterlockedExchange(&state->matched, 1);
        }
        UnmapViewOfFile(state);
        CloseHandle(stateMapping);
        return expected;
    }
}

extern "C" __declspec(dllexport) DWORD GetComponentAbiVersion() { return ComponentAbiVersion; }

extern "C" __declspec(dllexport) LRESULT CALLBACK OwnerTraceGetMessageHook(int code, WPARAM wParam, LPARAM lParam)
{
    if (code >= 0 && lParam != 0)
    {
        auto* message = reinterpret_cast<MSG*>(lParam);
        if (IsExpectedOwnerTraceMessage(*message))
        {
            message->message = WM_NULL;
            message->wParam = 0;
            message->lParam = 0;
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

extern "C" __declspec(dllexport) DWORD ProbeHotkey(UINT virtualKey, UINT modifiers, DWORD* win32ErrorCode)
{
    if (win32ErrorCode != nullptr) *win32ErrorCode = ERROR_SUCCESS;
    if (virtualKey == 0 || (modifiers & ~(MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_WIN)) != 0)
    {
        if (win32ErrorCode != nullptr) *win32ErrorCode = ERROR_INVALID_PARAMETER;
        return static_cast<DWORD>(ProbeStatus::ProbeError);
    }
    const auto window = CreateWindowExW(0, L"STATIC", L"KeyRadar.Native.HotkeyProbe", 0, 0, 0, 0, 0,
        HWND_MESSAGE, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (window == nullptr)
    {
        if (win32ErrorCode != nullptr) *win32ErrorCode = GetLastError();
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
    if (win32ErrorCode != nullptr) *win32ErrorCode = error;
    if (error == HotkeyAlreadyRegisteredError) return static_cast<DWORD>(ProbeStatus::Occupied);
    if (error == InvalidHotkeyError) return static_cast<DWORD>(ProbeStatus::SystemReserved);
    return static_cast<DWORD>(ProbeStatus::ProbeError);
}
