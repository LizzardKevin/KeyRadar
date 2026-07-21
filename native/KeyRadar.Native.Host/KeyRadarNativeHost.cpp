#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>
#include <array>
#include <string>
#include <vector>

namespace
{
    using GetComponentAbiVersionFunction = DWORD(*)();
    using ProbeHotkeyFunction = DWORD(*)(UINT, UINT, DWORD*);
    using OwnerTraceHookFunction = LRESULT(CALLBACK*)(int, WPARAM, LPARAM);

#if defined(_WIN64)
    constexpr wchar_t LibraryName[] = L"KeyRadar.Native.x64.dll";
#else
    constexpr wchar_t LibraryName[] = L"KeyRadar.Native.x86.dll";
#endif
    constexpr DWORD OwnerTraceMagic = 0x4B525452; // "KRTR"
    constexpr DWORD OwnerTraceVersion = 2;
    constexpr wchar_t OwnerTraceMappingPrefix[] = L"Local\\KeyRadar.OwnerTrace.State";
    constexpr wchar_t OwnerTraceLocatorPrefix[] = L"Local\\KeyRadar.OwnerTrace.Locator";

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

    struct TraceArguments
    {
        DWORD virtualKey{};
        DWORD modifiers{};
        DWORD timeoutMilliseconds{ 3000 };
        const wchar_t* resultPath{};
        const wchar_t* readyPath{};
        std::array<BYTE, 16> nonce{};
        bool hasNonce{};
        bool createTrace{};
        bool noInput{};
        bool trace{};
        bool selfTest{};
    };

    struct TraceContext
    {
        HANDLE stateMapping{};
        OwnerTraceState* state{};
        HANDLE locatorMapping{};
        OwnerTraceLocator* locator{};
        DWORD ownerHostProcessId{};
        ULONGLONG ownerHostStartTime{};
    };

    OwnerTraceHookFunction FindOwnerTraceHook(HMODULE library)
    {
        const auto undecorated = GetProcAddress(library, "OwnerTraceGetMessageHook");
        if (undecorated != nullptr) return reinterpret_cast<OwnerTraceHookFunction>(undecorated);
#if !defined(_WIN64)
        const auto stdcall = GetProcAddress(library, "OwnerTraceGetMessageHook@12");
        if (stdcall != nullptr) return reinterpret_cast<OwnerTraceHookFunction>(stdcall);
        const auto leadingUnderscoreStdcall = GetProcAddress(library, "_OwnerTraceGetMessageHook@12");
        if (leadingUnderscoreStdcall != nullptr) return reinterpret_cast<OwnerTraceHookFunction>(leadingUnderscoreStdcall);
#endif
        return nullptr;
    }

    bool TryParseUnsigned(const wchar_t* value, DWORD maximum, DWORD& result)
    {
        if (value == nullptr || *value == L'\0') return false;
        wchar_t* end = nullptr;
        const auto parsed = wcstoul(value, &end, 10);
        if (end == value || *end != L'\0' || parsed > maximum) return false;
        result = static_cast<DWORD>(parsed);
        return true;
    }

    bool TryParseNonce(const wchar_t* value, std::array<BYTE, 16>& nonce)
    {
        if (value == nullptr || wcslen(value) != 32) return false;
        for (size_t index = 0; index < nonce.size(); ++index)
        {
            wchar_t pair[3]{ value[index * 2], value[index * 2 + 1], L'\0' };
            wchar_t* end = nullptr;
            const auto parsed = wcstoul(pair, &end, 16);
            if (end == pair || *end != L'\0' || parsed > 0xFF) return false;
            nonce[index] = static_cast<BYTE>(parsed);
        }
        return true;
    }

    void FormatNonce(const BYTE nonce[16], wchar_t (&text)[33])
    {
        for (size_t index = 0; index < 16; ++index) swprintf_s(text + index * 2, 3, L"%02x", nonce[index]);
    }

    std::string FormatNonceText(const BYTE nonce[16])
    {
        wchar_t wideText[33]{};
        FormatNonce(nonce, wideText);
        std::string text;
        text.reserve(32);
        for (const auto character : wideText) if (character != L'\0') text.push_back(static_cast<char>(character));
        return text;
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

    ULONGLONG GetCurrentProcessStartTime()
    {
        FILETIME created{}, ignored{};
        if (!GetProcessTimes(GetCurrentProcess(), &created, &ignored, &ignored, &ignored)) return 0;
        ULARGE_INTEGER start{};
        start.LowPart = created.dwLowDateTime;
        start.HighPart = created.dwHighDateTime;
        return start.QuadPart;
    }

    bool IsExpected(const OwnerTraceLocator& locator, const TraceArguments& arguments)
    {
        return locator.magic == OwnerTraceMagic && locator.version == OwnerTraceVersion &&
            locator.virtualKey == arguments.virtualKey && locator.modifiers == arguments.modifiers &&
            memcmp(locator.nonce, arguments.nonce.data(), arguments.nonce.size()) == 0 &&
            locator.ownerHostProcessId != 0 && locator.ownerHostStartTime != 0 &&
            IsProcessStillRunning(locator.ownerHostProcessId, locator.ownerHostStartTime);
    }

    bool IsExpected(const OwnerTraceState& state, const OwnerTraceLocator& locator)
    {
        return state.magic == OwnerTraceMagic && state.version == OwnerTraceVersion &&
            state.ownerHostProcessId == locator.ownerHostProcessId &&
            state.ownerHostStartTime == locator.ownerHostStartTime &&
            state.virtualKey == locator.virtualKey && state.modifiers == locator.modifiers &&
            memcmp(state.nonce, locator.nonce, sizeof(state.nonce)) == 0;
    }

    void CloseTraceContext(TraceContext& context)
    {
        if (context.locator != nullptr) UnmapViewOfFile(context.locator);
        if (context.locatorMapping != nullptr) CloseHandle(context.locatorMapping);
        if (context.state != nullptr) UnmapViewOfFile(context.state);
        if (context.stateMapping != nullptr) CloseHandle(context.stateMapping);
        context = {};
    }

    bool CreateTraceContext(const TraceArguments& arguments, TraceContext& context, DWORD& errorCode)
    {
        wchar_t stateName[192]{};
        wchar_t locatorName[192]{};
        if (!BuildSessionName(OwnerTraceMappingPrefix, arguments.nonce.data(), true, stateName) ||
            !BuildSessionName(OwnerTraceLocatorPrefix, arguments.nonce.data(), false, locatorName))
        {
            errorCode = GetLastError();
            return false;
        }
        context.stateMapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(OwnerTraceState), stateName);
        if (context.stateMapping == nullptr || GetLastError() == ERROR_ALREADY_EXISTS)
        {
            errorCode = context.stateMapping == nullptr ? GetLastError() : ERROR_ALREADY_EXISTS;
            if (context.stateMapping != nullptr) CloseHandle(context.stateMapping);
            context.stateMapping = nullptr;
            return false;
        }
        context.state = static_cast<OwnerTraceState*>(MapViewOfFile(context.stateMapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(OwnerTraceState)));
        if (context.state == nullptr)
        {
            errorCode = GetLastError();
            CloseTraceContext(context);
            return false;
        }
        const auto ownerStartTime = GetCurrentProcessStartTime();
        if (ownerStartTime == 0)
        {
            errorCode = GetLastError();
            CloseTraceContext(context);
            return false;
        }
        ZeroMemory(context.state, sizeof(OwnerTraceState));
        context.state->magic = OwnerTraceMagic;
        context.state->version = OwnerTraceVersion;
        context.state->ownerHostProcessId = GetCurrentProcessId();
        context.state->ownerHostStartTime = ownerStartTime;
        context.state->virtualKey = arguments.virtualKey;
        context.state->modifiers = arguments.modifiers;
        memcpy(context.state->nonce, arguments.nonce.data(), arguments.nonce.size());

        context.locatorMapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(OwnerTraceLocator), locatorName);
        if (context.locatorMapping == nullptr || GetLastError() == ERROR_ALREADY_EXISTS)
        {
            errorCode = context.locatorMapping == nullptr ? GetLastError() : ERROR_ALREADY_EXISTS;
            if (context.locatorMapping != nullptr) CloseHandle(context.locatorMapping);
            context.locatorMapping = nullptr;
            CloseTraceContext(context);
            return false;
        }
        context.locator = static_cast<OwnerTraceLocator*>(MapViewOfFile(context.locatorMapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(OwnerTraceLocator)));
        if (context.locator == nullptr)
        {
            errorCode = GetLastError();
            CloseTraceContext(context);
            return false;
        }
        ZeroMemory(context.locator, sizeof(OwnerTraceLocator));
        context.locator->magic = OwnerTraceMagic;
        context.locator->version = OwnerTraceVersion;
        context.locator->ownerHostProcessId = context.state->ownerHostProcessId;
        context.locator->ownerHostStartTime = ownerStartTime;
        context.locator->virtualKey = arguments.virtualKey;
        context.locator->modifiers = arguments.modifiers;
        memcpy(context.locator->nonce, arguments.nonce.data(), arguments.nonce.size());
        context.ownerHostProcessId = context.state->ownerHostProcessId;
        context.ownerHostStartTime = ownerStartTime;
        return true;
    }

    bool OpenTraceContext(const TraceArguments& arguments, TraceContext& context, DWORD& errorCode)
    {
        wchar_t stateName[192]{};
        wchar_t locatorName[192]{};
        if (!BuildSessionName(OwnerTraceMappingPrefix, arguments.nonce.data(), true, stateName) ||
            !BuildSessionName(OwnerTraceLocatorPrefix, arguments.nonce.data(), false, locatorName))
        {
            errorCode = GetLastError();
            return false;
        }
        const auto locatorMapping = OpenFileMappingW(FILE_MAP_READ, FALSE, locatorName);
        if (locatorMapping == nullptr) { errorCode = GetLastError(); return false; }
        const auto locator = static_cast<OwnerTraceLocator*>(MapViewOfFile(locatorMapping, FILE_MAP_READ, 0, 0, sizeof(OwnerTraceLocator)));
        if (locator == nullptr)
        {
            errorCode = GetLastError();
            CloseHandle(locatorMapping);
            return false;
        }
        const auto expected = IsExpected(*locator, arguments);
        OwnerTraceLocator verifiedLocator{};
        if (expected) memcpy(&verifiedLocator, locator, sizeof(verifiedLocator));
        UnmapViewOfFile(locator);
        CloseHandle(locatorMapping);
        if (!expected) { errorCode = ERROR_INVALID_DATA; return false; }
        context.stateMapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, stateName);
        if (context.stateMapping == nullptr) { errorCode = GetLastError(); return false; }
        context.state = static_cast<OwnerTraceState*>(MapViewOfFile(context.stateMapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(OwnerTraceState)));
        if (context.state == nullptr)
        {
            errorCode = GetLastError();
            CloseTraceContext(context);
            return false;
        }
        if (!IsExpected(*context.state, verifiedLocator))
        {
            errorCode = ERROR_INVALID_DATA;
            CloseTraceContext(context);
            return false;
        }
        context.ownerHostProcessId = verifiedLocator.ownerHostProcessId;
        context.ownerHostStartTime = verifiedLocator.ownerHostStartTime;
        return true;
    }

    int WriteTextFile(const wchar_t* path, const std::string& text)
    {
        const auto file = CreateFileW(path, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return 7;
        DWORD written = 0;
        const auto succeeded = WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr);
        FlushFileBuffers(file);
        CloseHandle(file);
        return succeeded && written == static_cast<DWORD>(text.size()) ? 0 : 8;
    }

    int WriteProbeResult(const wchar_t* path, DWORD status, DWORD errorCode)
    {
        return WriteTextFile(path, std::to_string(status) + "," + std::to_string(errorCode));
    }

    int WriteTraceResult(const wchar_t* path, DWORD status, DWORD processId, DWORD threadId, DWORD errorCode,
        const TraceArguments& arguments, const TraceContext& context)
    {
        return WriteTextFile(path, std::to_string(status) + "," + std::to_string(processId) + "," +
            std::to_string(threadId) + "," + std::to_string(errorCode) + "," +
            std::to_string(OwnerTraceMagic) + "," + std::to_string(OwnerTraceVersion) + "," +
            std::to_string(context.ownerHostProcessId) + "," + std::to_string(context.ownerHostStartTime) + "," +
            std::to_string(arguments.virtualKey) + "," + std::to_string(arguments.modifiers) + "," +
            FormatNonceText(arguments.nonce.data()));
    }

    INPUT MakeKeyboardInput(WORD virtualKey, DWORD flags = 0)
    {
        INPUT input{};
        input.type = INPUT_KEYBOARD;
        input.ki.wVk = virtualKey;
        input.ki.dwFlags = flags;
        return input;
    }

    bool IsAnyKeyDown(std::initializer_list<int> keys)
    {
        for (const auto key : keys) if ((GetAsyncKeyState(key) & 0x8000) != 0) return true;
        return false;
    }

    DWORD SendTraceInput(DWORD virtualKey, DWORD modifiers)
    {
        // Never synthesize while a user is holding a modifier or the target key; this
        // avoids extending a real chord or releasing a key state we do not own.
        if (IsAnyKeyDown({ VK_CONTROL, VK_LCONTROL, VK_RCONTROL, VK_MENU, VK_LMENU, VK_RMENU,
            VK_SHIFT, VK_LSHIFT, VK_RSHIFT, VK_LWIN, VK_RWIN, static_cast<int>(virtualKey) }))
        {
            return ERROR_BUSY;
        }
        std::vector<WORD> modifierKeys;
        if ((modifiers & MOD_CONTROL) != 0) modifierKeys.push_back(VK_CONTROL);
        if ((modifiers & MOD_SHIFT) != 0) modifierKeys.push_back(VK_SHIFT);
        if ((modifiers & MOD_ALT) != 0) modifierKeys.push_back(VK_MENU);
        if ((modifiers & MOD_WIN) != 0) modifierKeys.push_back(VK_LWIN);
        std::vector<INPUT> inputs;
        inputs.reserve(modifierKeys.size() * 2 + 2);
        for (const auto key : modifierKeys) inputs.push_back(MakeKeyboardInput(key));
        inputs.push_back(MakeKeyboardInput(static_cast<WORD>(virtualKey)));
        inputs.push_back(MakeKeyboardInput(static_cast<WORD>(virtualKey), KEYEVENTF_KEYUP));
        for (auto iterator = modifierKeys.rbegin(); iterator != modifierKeys.rend(); ++iterator)
            inputs.push_back(MakeKeyboardInput(*iterator, KEYEVENTF_KEYUP));
        const auto sent = SendInput(static_cast<UINT>(inputs.size()), inputs.data(), sizeof(INPUT));
        if (sent == static_cast<UINT>(inputs.size())) return ERROR_SUCCESS;
        const auto error = GetLastError();
        return error == ERROR_SUCCESS ? ERROR_GEN_FAILURE : error;
    }

    int RunTrace(const TraceArguments& arguments, HMODULE library, OwnerTraceHookFunction hook)
    {
        DWORD errorCode = ERROR_SUCCESS;
        TraceContext context{};
        const auto opened = arguments.createTrace
            ? CreateTraceContext(arguments, context, errorCode)
            : OpenTraceContext(arguments, context, errorCode);
        if (!opened) return arguments.noInput ? 9 : 12;
        const auto installed = SetWindowsHookExW(WH_GETMESSAGE, hook, library, 0);
        if (installed == nullptr)
        {
            errorCode = GetLastError();
            const auto write = arguments.noInput ? 11 : WriteTraceResult(arguments.resultPath, 2, 0, 0, errorCode, arguments, context);
            CloseTraceContext(context);
            return write;
        }
        if (arguments.readyPath != nullptr && WriteTextFile(arguments.readyPath, "ready") != 0)
        {
            UnhookWindowsHookEx(installed);
            CloseTraceContext(context);
            return 10;
        }
        if (!arguments.noInput)
        {
            Sleep(75);
            const auto inputError = SendTraceInput(arguments.virtualKey, arguments.modifiers);
            if (inputError != ERROR_SUCCESS)
            {
                UnhookWindowsHookEx(installed);
                const auto write = WriteTraceResult(arguments.resultPath, 2, 0, 0, inputError, arguments, context);
                CloseTraceContext(context);
                return write;
            }
        }
        const auto deadline = GetTickCount64() + arguments.timeoutMilliseconds;
        while (InterlockedCompareExchange(&context.state->matched, 0, 0) == 0 && GetTickCount64() < deadline) Sleep(15);
        const auto matched = InterlockedCompareExchange(&context.state->matched, 0, 0) != 0;
        const auto processId = static_cast<DWORD>(InterlockedCompareExchange(&context.state->processId, 0, 0));
        const auto threadId = static_cast<DWORD>(InterlockedCompareExchange(&context.state->threadId, 0, 0));
        const auto ownerStillTrusted = IsProcessStillRunning(context.ownerHostProcessId, context.ownerHostStartTime);
        UnhookWindowsHookEx(installed);
        if (arguments.noInput) { CloseTraceContext(context); return 0; }
        const auto write = WriteTraceResult(arguments.resultPath,
            ownerStillTrusted ? (matched ? 1 : 0) : 2,
            ownerStillTrusted ? processId : 0, ownerStillTrusted ? threadId : 0,
            ownerStillTrusted ? ERROR_SUCCESS : ERROR_INVALID_DATA, arguments, context);
        CloseTraceContext(context);
        return write;
    }
}

int wmain(int argumentCount, wchar_t* commandLine[])
{
    TraceArguments arguments{};
    for (int index = 1; index < argumentCount; ++index)
    {
        if (wcscmp(commandLine[index], L"--self-test") == 0) { arguments.selfTest = true; continue; }
        if (wcscmp(commandLine[index], L"--trace") == 0) { arguments.trace = true; continue; }
        if (wcscmp(commandLine[index], L"--no-input") == 0) { arguments.noInput = true; continue; }
        if (wcscmp(commandLine[index], L"--create-trace") == 0) { arguments.createTrace = true; continue; }
        if (index + 1 >= argumentCount) return 2;
        if (wcscmp(commandLine[index], L"--vk") == 0) { if (!TryParseUnsigned(commandLine[++index], 0xFF, arguments.virtualKey)) return 2; }
        else if (wcscmp(commandLine[index], L"--mod") == 0) { if (!TryParseUnsigned(commandLine[++index], 0xF, arguments.modifiers)) return 2; }
        else if (wcscmp(commandLine[index], L"--timeout-ms") == 0)
        {
            if (!TryParseUnsigned(commandLine[++index], 10000, arguments.timeoutMilliseconds) || arguments.timeoutMilliseconds == 0) return 2;
        }
        else if (wcscmp(commandLine[index], L"--nonce") == 0)
        {
            if (!TryParseNonce(commandLine[++index], arguments.nonce)) return 2;
            arguments.hasNonce = true;
        }
        else if (wcscmp(commandLine[index], L"--result") == 0) arguments.resultPath = commandLine[++index];
        else if (wcscmp(commandLine[index], L"--ready") == 0) arguments.readyPath = commandLine[++index];
        else return 2;
    }
    wchar_t executablePath[MAX_PATH]{};
    if (GetModuleFileNameW(nullptr, executablePath, MAX_PATH) == 0) return 3;
    auto* fileName = wcsrchr(executablePath, L'\\');
    if (fileName == nullptr) return 3;
    *(fileName + 1) = L'\0';
    const std::wstring libraryPath = std::wstring(executablePath) + LibraryName;
    const auto library = LoadLibraryW(libraryPath.c_str());
    if (library == nullptr) return 4;
    const auto getAbiVersion = reinterpret_cast<GetComponentAbiVersionFunction>(GetProcAddress(library, "GetComponentAbiVersion"));
    const auto probeHotkey = reinterpret_cast<ProbeHotkeyFunction>(GetProcAddress(library, "ProbeHotkey"));
    const auto ownerTraceHook = FindOwnerTraceHook(library);
    if (getAbiVersion == nullptr || probeHotkey == nullptr || ownerTraceHook == nullptr) { FreeLibrary(library); return 5; }
    if (arguments.selfTest) { const auto result = getAbiVersion() == 1 ? 0 : 9; FreeLibrary(library); return result; }
    if (arguments.virtualKey == 0 || arguments.resultPath == nullptr || (arguments.trace && !arguments.hasNonce)) { FreeLibrary(library); return 2; }
    DWORD errorCode = ERROR_SUCCESS;
    const auto result = arguments.trace
        ? RunTrace(arguments, library, ownerTraceHook)
        : WriteProbeResult(arguments.resultPath, probeHotkey(arguments.virtualKey, arguments.modifiers, &errorCode), errorCode);
    FreeLibrary(library);
    return result;
}
