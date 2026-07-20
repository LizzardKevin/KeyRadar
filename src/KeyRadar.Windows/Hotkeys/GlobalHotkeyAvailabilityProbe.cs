using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.Hotkeys;

public sealed class GlobalHotkeyAvailabilityProbe(
    IHotkeyRegistrationApi registrationApi,
    Func<DateTimeOffset>? utcNow = null)
{
    private static int _nextIdentifier = 0x4B00;
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public HotkeyProbeResult Probe(HotkeyGesture gesture)
    {
        var identifier = Interlocked.Increment(ref _nextIdentifier);
        var registration = registrationApi.TryRegister(identifier, gesture);

        if (registration.Attempt == HotkeyRegistrationAttempt.Registered)
        {
            try
            {
                return CreateResult(gesture, HotkeyProbeAvailability.AvailableAtScanTime);
            }
            finally
            {
                registrationApi.Unregister(identifier);
            }
        }

        var availability = registration.Attempt switch
        {
            HotkeyRegistrationAttempt.AlreadyRegistered => HotkeyProbeAvailability.Occupied,
            HotkeyRegistrationAttempt.SystemReserved => HotkeyProbeAvailability.SystemReserved,
            _ => HotkeyProbeAvailability.ProbeError,
        };
        return CreateResult(gesture, availability, registration.Win32ErrorCode);
    }

    private HotkeyProbeResult CreateResult(
        HotkeyGesture gesture,
        HotkeyProbeAvailability availability,
        int? errorCode = null) =>
        new(
            gesture,
            availability,
            HotkeyProbeMechanism.RegisterHotKeyProbe,
            HotkeyOwner.Unknown,
            _utcNow(),
            errorCode);
}
