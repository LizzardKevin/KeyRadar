#define WIN32_LEAN_AND_MEAN
#include <windows.h>

namespace
{
    constexpr DWORD MappingMagic = 0x4B524144;
    constexpr UINT NoRepeatModifier = 0x4000;

#if defined(_WIN64)
    constexpr wchar_t MappingName[] = L"Local\\LizzardKevin.KeyRadar.DeepConfirmation.x64.Map";
    constexpr wchar_t EventName[] = L"Local\\LizzardKevin.KeyRadar.DeepConfirmation.x64.Event";
#else
    constexpr wchar_t MappingName[] = L"Local\\LizzardKevin.KeyRadar.DeepConfirmation.x86.Map";
    constexpr wchar_t EventName[] = L"Local\\LizzardKevin.KeyRadar.DeepConfirmation.x86.Event";
#endif

    struct SharedObservation
    {
        DWORD magic;
        volatile LONG enabled;
        DWORD virtualKey;
        DWORD modifiers;
        volatile LONG processId;
    };

    HINSTANCE moduleInstance = nullptr;
    HHOOK messageHook = nullptr;
    HANDLE mappingHandle = nullptr;
    HANDLE observedEvent = nullptr;
    SharedObservation* sharedObservation = nullptr;

    LRESULT CALLBACK ObserveMessage(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0 && lParam != 0)
        {
            const auto* message = reinterpret_cast<const MSG*>(lParam);
            if (message->message == WM_HOTKEY)
            {
                const auto mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, MappingName);
                if (mapping != nullptr)
                {
                    auto* observation = static_cast<SharedObservation*>(
                        MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(SharedObservation)));
                    if (observation != nullptr)
                    {
                        const auto modifiers = LOWORD(message->lParam) & ~NoRepeatModifier;
                        const auto virtualKey = HIWORD(message->lParam);
                        if (observation->magic == MappingMagic &&
                            observation->enabled != 0 &&
                            observation->virtualKey == virtualKey &&
                            observation->modifiers == modifiers)
                        {
                            if (InterlockedCompareExchange(
                                    &observation->processId,
                                    static_cast<LONG>(GetCurrentProcessId()),
                                    0) == 0)
                            {
                                const auto eventHandle = OpenEventW(EVENT_MODIFY_STATE, FALSE, EventName);
                                if (eventHandle != nullptr)
                                {
                                    SetEvent(eventHandle);
                                    CloseHandle(eventHandle);
                                }
                            }
                        }

                        UnmapViewOfFile(observation);
                    }

                    CloseHandle(mapping);
                }
            }
        }

        return CallNextHookEx(messageHook, code, wParam, lParam);
    }

    void ReleaseObservation()
    {
        if (messageHook != nullptr)
        {
            UnhookWindowsHookEx(messageHook);
            messageHook = nullptr;
        }

        if (sharedObservation != nullptr)
        {
            InterlockedExchange(&sharedObservation->enabled, 0);
            UnmapViewOfFile(sharedObservation);
            sharedObservation = nullptr;
        }

        if (observedEvent != nullptr)
        {
            CloseHandle(observedEvent);
            observedEvent = nullptr;
        }

        if (mappingHandle != nullptr)
        {
            CloseHandle(mappingHandle);
            mappingHandle = nullptr;
        }
    }
}

extern "C" __declspec(dllexport) BOOL StartObservation(UINT virtualKey, UINT modifiers)
{
    if (messageHook != nullptr || virtualKey == 0)
    {
        SetLastError(ERROR_INVALID_STATE);
        return FALSE;
    }

    mappingHandle = CreateFileMappingW(
        INVALID_HANDLE_VALUE,
        nullptr,
        PAGE_READWRITE,
        0,
        sizeof(SharedObservation),
        MappingName);
    if (mappingHandle == nullptr)
    {
        return FALSE;
    }

    sharedObservation = static_cast<SharedObservation*>(
        MapViewOfFile(mappingHandle, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(SharedObservation)));
    if (sharedObservation == nullptr)
    {
        ReleaseObservation();
        return FALSE;
    }

    observedEvent = CreateEventW(nullptr, TRUE, FALSE, EventName);
    if (observedEvent == nullptr)
    {
        ReleaseObservation();
        return FALSE;
    }

    sharedObservation->magic = MappingMagic;
    sharedObservation->virtualKey = virtualKey;
    sharedObservation->modifiers = modifiers & ~NoRepeatModifier;
    InterlockedExchange(&sharedObservation->processId, 0);
    InterlockedExchange(&sharedObservation->enabled, 1);
    ResetEvent(observedEvent);

    messageHook = SetWindowsHookExW(WH_GETMESSAGE, ObserveMessage, moduleInstance, 0);
    if (messageHook == nullptr)
    {
        ReleaseObservation();
        return FALSE;
    }

    return TRUE;
}

extern "C" __declspec(dllexport) DWORD WaitForObservation(DWORD timeoutMilliseconds)
{
    if (observedEvent == nullptr)
    {
        return WAIT_FAILED;
    }

    const auto started = GetTickCount64();
    while (true)
    {
        const auto elapsed = GetTickCount64() - started;
        const auto remaining = elapsed >= timeoutMilliseconds
            ? 0
            : timeoutMilliseconds - static_cast<DWORD>(elapsed);
        const auto waitResult = MsgWaitForMultipleObjects(
            1,
            &observedEvent,
            FALSE,
            remaining,
            QS_ALLINPUT);
        if (waitResult == WAIT_OBJECT_0 || waitResult == WAIT_TIMEOUT || waitResult == WAIT_FAILED)
        {
            return waitResult;
        }

        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE))
        {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
}

extern "C" __declspec(dllexport) DWORD GetObservedProcessId()
{
    return sharedObservation == nullptr
        ? 0
        : static_cast<DWORD>(InterlockedCompareExchange(&sharedObservation->processId, 0, 0));
}

extern "C" __declspec(dllexport) void StopObservation()
{
    ReleaseObservation();
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        moduleInstance = instance;
        DisableThreadLibraryCalls(instance);
    }

    return TRUE;
}
