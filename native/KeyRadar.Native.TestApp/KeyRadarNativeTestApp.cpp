#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>

namespace
{
    bool ParseUnsigned(const wchar_t* value, DWORD maximum, DWORD& result)
    {
        wchar_t* end = nullptr;
        const auto parsed = wcstoul(value, &end, 10);
        if (end == value || *end != L'\0' || parsed > maximum) return false;
        result = static_cast<DWORD>(parsed);
        return true;
    }
}

int wmain(int argumentCount, wchar_t* arguments[])
{
    DWORD virtualKey = 0;
    DWORD modifiers = 0;
    DWORD holdMilliseconds = 0;
    for (int index = 1; index + 1 < argumentCount; index += 2)
    {
        if (wcscmp(arguments[index], L"--vk") == 0)
        {
            if (!ParseUnsigned(arguments[index + 1], 0xFF, virtualKey)) return 2;
        }
        else if (wcscmp(arguments[index], L"--mod") == 0)
        {
            if (!ParseUnsigned(arguments[index + 1], 0xF, modifiers)) return 2;
        }
        else if (wcscmp(arguments[index], L"--hold-ms") == 0)
        {
            if (!ParseUnsigned(arguments[index + 1], 30000, holdMilliseconds)) return 2;
        }
        else
        {
            return 2;
        }
    }

    if (virtualKey == 0) return 2;
    MSG message{};
    PeekMessageW(&message, nullptr, WM_USER, WM_USER, PM_NOREMOVE);
    if (!RegisterHotKey(nullptr, 1, modifiers | MOD_NOREPEAT, virtualKey)) return 3;

    if (holdMilliseconds > 0)
    {
        Sleep(holdMilliseconds);
        UnregisterHotKey(nullptr, 1);
        return 0;
    }

    const auto posted = PostThreadMessageW(
        GetCurrentThreadId(),
        WM_HOTKEY,
        1,
        MAKELPARAM(modifiers, virtualKey));
    if (!posted)
    {
        UnregisterHotKey(nullptr, 1);
        return 4;
    }

    const auto result = GetMessageW(&message, nullptr, 0, 0);
    UnregisterHotKey(nullptr, 1);
    return result >= 0 && message.message == WM_HOTKEY ? 0 : 5;
}
