using KeyRadar.Hotkeys;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Hotkeys;

public sealed class GlobalHotkeyAvailabilityProbeTests
{
    [Fact]
    public void Free_hotkey_is_released_immediately_after_the_probe()
    {
        var api = new StubRegistrationApi(HotkeyRegistrationAttempt.Registered);
        var probe = new GlobalHotkeyAvailabilityProbe(api, () => ScanTime);

        var result = probe.Probe(HotkeyGesture.Parse("Alt+A"));

        Assert.Equal(HotkeyProbeAvailability.AvailableAtScanTime, result.Availability);
        Assert.Equal(HotkeyProbeMechanism.RegisterHotKeyProbe, result.Mechanism);
        Assert.Equal(HotkeyOwner.Unknown, result.Owner);
        Assert.Equal(ScanTime, result.ScannedAtUtc);
        Assert.Null(result.Win32ErrorCode);
        Assert.Equal(1, api.UnregisterCalls);
    }

    [Fact]
    public void Occupied_hotkey_is_never_unregistered_by_KeyRadar()
    {
        var api = new StubRegistrationApi(HotkeyRegistrationAttempt.AlreadyRegistered, 1409);
        var probe = new GlobalHotkeyAvailabilityProbe(api, () => ScanTime);

        var result = probe.Probe(HotkeyGesture.Parse("Alt+A"));

        Assert.Equal(HotkeyProbeAvailability.Occupied, result.Availability);
        Assert.Equal(1409, result.Win32ErrorCode);
        Assert.Equal(0, api.UnregisterCalls);
    }

    [Theory]
    [InlineData(HotkeyRegistrationAttempt.SystemReserved, HotkeyProbeAvailability.SystemReserved)]
    [InlineData(HotkeyRegistrationAttempt.Error, HotkeyProbeAvailability.ProbeError)]
    public void Failures_preserve_the_category_and_error_code(
        HotkeyRegistrationAttempt attempt,
        HotkeyProbeAvailability expected)
    {
        var probe = new GlobalHotkeyAvailabilityProbe(new StubRegistrationApi(attempt, 87), () => ScanTime);

        var result = probe.Probe(HotkeyGesture.Parse("Ctrl+F24"));

        Assert.Equal(expected, result.Availability);
        Assert.Equal(87, result.Win32ErrorCode);
    }

    private static readonly DateTimeOffset ScanTime = DateTimeOffset.Parse("2026-07-20T12:00:00Z");

    private sealed class StubRegistrationApi(HotkeyRegistrationAttempt attempt, int? errorCode = null)
        : IHotkeyRegistrationApi
    {
        public int UnregisterCalls { get; private set; }

        public HotkeyRegistrationResult TryRegister(int identifier, HotkeyGesture gesture) =>
            new(attempt, errorCode);

        public void Unregister(int identifier) => UnregisterCalls++;
    }
}
