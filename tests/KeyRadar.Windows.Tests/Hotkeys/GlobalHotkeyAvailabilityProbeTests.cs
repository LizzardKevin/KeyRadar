using KeyRadar.Shortcuts;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Hotkeys;

public sealed class GlobalHotkeyAvailabilityProbeTests
{
    [Fact]
    public void Free_shortcut_is_released_immediately_after_the_probe()
    {
        var api = new StubRegistrationApi(HotkeyRegistrationAttempt.Registered);
        var probe = new GlobalHotkeyAvailabilityProbe(api);

        var result = probe.Probe(ShortcutGesture.Parse("Alt+A"));

        Assert.Equal(GlobalHotkeyAvailability.Available, result);
        Assert.Equal(1, api.UnregisterCalls);
    }

    [Fact]
    public void Occupied_shortcut_is_never_unregistered_by_KeyRadar()
    {
        var api = new StubRegistrationApi(HotkeyRegistrationAttempt.AlreadyRegistered);
        var probe = new GlobalHotkeyAvailabilityProbe(api);

        var result = probe.Probe(ShortcutGesture.Parse("Alt+A"));

        Assert.Equal(GlobalHotkeyAvailability.Occupied, result);
        Assert.Equal(0, api.UnregisterCalls);
    }

    [Fact]
    public void Unsupported_shortcut_is_reported_without_registration_cleanup()
    {
        var api = new StubRegistrationApi(HotkeyRegistrationAttempt.Unsupported);
        var probe = new GlobalHotkeyAvailabilityProbe(api);

        Assert.Equal(
            GlobalHotkeyAvailability.Unsupported,
            probe.Probe(ShortcutGesture.Parse("Ctrl+F24")));
        Assert.Equal(0, api.UnregisterCalls);
    }

    private sealed class StubRegistrationApi(HotkeyRegistrationAttempt attempt) : IHotkeyRegistrationApi
    {
        public int UnregisterCalls { get; private set; }

        public HotkeyRegistrationAttempt TryRegister(int identifier, ShortcutGesture gesture) => attempt;

        public void Unregister(int identifier) => UnregisterCalls++;
    }
}
