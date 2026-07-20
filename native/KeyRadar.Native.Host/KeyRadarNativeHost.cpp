#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>
#include <string>

namespace
{
    using GetComponentAbiVersionFunction = DWORD(*)();
    using ProbeHotkeyFunction = DWORD(*)(UINT, UINT, DWORD*);

#if defined(_WIN64)
    constexpr wchar_t LibraryName[] = L"KeyRadar.Native.x64.dll";
#else
    constexpr wchar_t LibraryName[] = L"KeyRadar.Native.x86.dll";
#endif

    bool TryParseUnsigned(const wchar_t* value, DWORD maximum, DWORD& result)
    {
        if (value == nullptr || *value == L'\0') return false;
        wchar_t* end = nullptr;
        const auto parsed = wcstoul(value, &end, 10);
        if (end == value || *end != L'\0' || parsed > maximum) return false;
        result = static_cast<DWORD>(parsed);
        return true;
    }

    int WriteResult(const wchar_t* path, DWORD status, DWORD errorCode)
    {
        const auto file = CreateFileW(
            path,
            GENERIC_WRITE,
            0,
            nullptr,
            CREATE_NEW,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);
        if (file == INVALID_HANDLE_VALUE) return 7;

        const auto text = std::to_string(status) + "," + std::to_string(errorCode);
        DWORD written = 0;
        const auto succeeded = WriteFile(
            file,
            text.data(),
            static_cast<DWORD>(text.size()),
            &written,
            nullptr);
        FlushFileBuffers(file);
        CloseHandle(file);
        return succeeded && written == text.size() ? 0 : 8;
    }
}

int wmain(int argumentCount, wchar_t* arguments[])
{
    DWORD virtualKey = 0;
    DWORD modifiers = 0;
    const wchar_t* resultPath = nullptr;
    bool selfTest = false;

    for (int index = 1; index < argumentCount; ++index)
    {
        if (wcscmp(arguments[index], L"--self-test") == 0)
        {
            selfTest = true;
            continue;
        }
        if (index + 1 >= argumentCount) return 2;
        if (wcscmp(arguments[index], L"--vk") == 0)
        {
            if (!TryParseUnsigned(arguments[++index], 0xFF, virtualKey)) return 2;
        }
        else if (wcscmp(arguments[index], L"--mod") == 0)
        {
            if (!TryParseUnsigned(arguments[++index], 0xF, modifiers)) return 2;
        }
        else if (wcscmp(arguments[index], L"--result") == 0)
        {
            resultPath = arguments[++index];
        }
        else
        {
            return 2;
        }
    }

    wchar_t executablePath[MAX_PATH]{};
    if (GetModuleFileNameW(nullptr, executablePath, MAX_PATH) == 0) return 3;
    auto* fileName = wcsrchr(executablePath, L'\\');
    if (fileName == nullptr) return 3;
    *(fileName + 1) = L'\0';
    const std::wstring libraryPath = std::wstring(executablePath) + LibraryName;

    const auto library = LoadLibraryW(libraryPath.c_str());
    if (library == nullptr) return 4;
    const auto getAbiVersion = reinterpret_cast<GetComponentAbiVersionFunction>(
        GetProcAddress(library, "GetComponentAbiVersion"));
    const auto probeHotkey = reinterpret_cast<ProbeHotkeyFunction>(
        GetProcAddress(library, "ProbeHotkey"));
    if (getAbiVersion == nullptr || probeHotkey == nullptr)
    {
        FreeLibrary(library);
        return 5;
    }

    if (selfTest)
    {
        const auto result = getAbiVersion() == 1 ? 0 : 9;
        FreeLibrary(library);
        return result;
    }
    if (virtualKey == 0 || resultPath == nullptr)
    {
        FreeLibrary(library);
        return 2;
    }

    DWORD errorCode = ERROR_SUCCESS;
    const auto status = probeHotkey(virtualKey, modifiers, &errorCode);
    const auto result = WriteResult(resultPath, status, errorCode);
    FreeLibrary(library);
    return result;
}
