using KeyRadar.Shortcuts;

namespace KeyRadar.Windows.Hotkeys;

public sealed class GlobalHotkeyAvailabilityProbe(IHotkeyRegistrationApi registrationApi)
{
    private static int _nextIdentifier = 0x4B00;

    public GlobalHotkeyAvailability Probe(ShortcutGesture gesture)
    {
        var identifier = Interlocked.Increment(ref _nextIdentifier);
        var attempt = registrationApi.TryRegister(identifier, gesture);

        if (attempt == HotkeyRegistrationAttempt.Registered)
        {
            try
            {
                return GlobalHotkeyAvailability.Available;
            }
            finally
            {
                registrationApi.Unregister(identifier);
            }
        }

        return attempt == HotkeyRegistrationAttempt.AlreadyRegistered
            ? GlobalHotkeyAvailability.Occupied
            : GlobalHotkeyAvailability.Unsupported;
    }
}
