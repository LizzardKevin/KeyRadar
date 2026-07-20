using KeyRadar.Hotkeys;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Hotkeys;

public sealed class GlobalHotkeyOccupancyScannerTests
{
    [Fact]
    public async Task Scanner_is_batched_reports_progress_and_keeps_unknown_occupied_items()
    {
        var gestures = new[]
        {
            HotkeyGesture.Parse("Alt+A"),
            HotkeyGesture.Parse("Ctrl+L"),
            HotkeyGesture.Parse("F1"),
        };
        var api = new SequenceRegistrationApi(
            HotkeyRegistrationAttempt.AlreadyRegistered,
            HotkeyRegistrationAttempt.Registered,
            HotkeyRegistrationAttempt.SystemReserved);
        var progress = new List<HotkeyScanProgress>();
        var scanner = new GlobalHotkeyOccupancyScanner(
            new GlobalHotkeyAvailabilityProbe(api),
            new AlwaysSafeGate(),
            batchSize: 2);

        var results = await scanner.ScanAsync(
            gestures,
            new InlineProgress<HotkeyScanProgress>(progress.Add),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, results.Count);
        Assert.Equal(HotkeyProbeAvailability.Occupied, results[0].Availability);
        Assert.Equal(HotkeyOwner.Unknown, results[0].Owner);
        Assert.Equal(3, progress[^1].Completed);
        Assert.Equal(3, progress[^1].Total);
        Assert.Equal(1, api.UnregisterCalls);
    }

    [Fact]
    public async Task Scanner_stops_before_registration_when_the_desktop_is_not_safe()
    {
        var api = new SequenceRegistrationApi(HotkeyRegistrationAttempt.Registered);
        var scanner = new GlobalHotkeyOccupancyScanner(
            new GlobalHotkeyAvailabilityProbe(api),
            new CancelGate());

        await Assert.ThrowsAsync<HotkeyScanSafetyException>(() => scanner.ScanAsync(
            [HotkeyGesture.Parse("Alt+A")],
            progress: null,
            TestContext.Current.CancellationToken));

        Assert.Equal(0, api.RegisterCalls);
    }

    [Fact]
    public async Task Scanner_cancels_instead_of_waiting_forever_for_a_held_key()
    {
        var api = new SequenceRegistrationApi(HotkeyRegistrationAttempt.Registered);
        var scanner = new GlobalHotkeyOccupancyScanner(
            new GlobalHotkeyAvailabilityProbe(api),
            new AlwaysPauseGate(),
            maximumInputPause: TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<HotkeyScanSafetyException>(() => scanner.ScanAsync(
            [HotkeyGesture.Parse("Alt+A")],
            progress: null,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, api.RegisterCalls);
    }

    [Fact]
    public void Safety_gate_pauses_for_keyboard_input_but_ignores_mouse_buttons()
    {
        var mouseOnly = new WindowsHotkeyProbeSafetyGate(
            virtualKey => virtualKey == 0x01 ? unchecked((short)0x8000) : (short)0,
            isDefaultDesktop: () => true);
        var keyboard = new WindowsHotkeyProbeSafetyGate(
            virtualKey => virtualKey == 0x41 ? unchecked((short)0x8000) : (short)0,
            isDefaultDesktop: () => true);

        Assert.Equal(HotkeyProbeSafety.Safe, mouseOnly.Check());
        Assert.Equal(HotkeyProbeSafety.Pause, keyboard.Check());
    }

    [Fact]
    public void Safety_gate_cancels_outside_the_default_desktop()
    {
        var gate = new WindowsHotkeyProbeSafetyGate(_ => 0, isDefaultDesktop: () => false);

        Assert.Equal(HotkeyProbeSafety.Cancel, gate.Check());
    }

    [Fact]
    public void Safety_gate_can_ignore_only_an_explicitly_skipped_extended_function_key()
    {
        var held = WindowsHotkeyProbeSafetyGate.CaptureHeldExtendedFunctionKeys(
            virtualKey => virtualKey == 0x85 ? unchecked((short)0x8000) : (short)0);
        var gate = new WindowsHotkeyProbeSafetyGate(
            virtualKey => virtualKey == 0x85 ? unchecked((short)0x8000) : (short)0,
            isDefaultDesktop: () => true,
            ignoredVirtualKeys: held);
        var textKeyGate = new WindowsHotkeyProbeSafetyGate(
            virtualKey => virtualKey == 0x41 ? unchecked((short)0x8000) : (short)0,
            isDefaultDesktop: () => true,
            ignoredVirtualKeys: held);

        Assert.Contains(0x85, held);
        Assert.Equal(HotkeyProbeSafety.Safe, gate.Check());
        Assert.Equal(HotkeyProbeSafety.Pause, textKeyGate.Check());
    }

    private sealed class SequenceRegistrationApi(params HotkeyRegistrationAttempt[] attempts)
        : IHotkeyRegistrationApi
    {
        private int _index;
        public int RegisterCalls { get; private set; }
        public int UnregisterCalls { get; private set; }

        public HotkeyRegistrationResult TryRegister(int identifier, HotkeyGesture gesture)
        {
            RegisterCalls++;
            var attempt = attempts[Math.Min(_index++, attempts.Length - 1)];
            return new HotkeyRegistrationResult(attempt, attempt == HotkeyRegistrationAttempt.AlreadyRegistered ? 1409 : null);
        }

        public void Unregister(int identifier) => UnregisterCalls++;
    }

    private sealed class AlwaysSafeGate : IHotkeyProbeSafetyGate
    {
        public HotkeyProbeSafety Check() => HotkeyProbeSafety.Safe;
    }

    private sealed class CancelGate : IHotkeyProbeSafetyGate
    {
        public HotkeyProbeSafety Check() => HotkeyProbeSafety.Cancel;
    }

    private sealed class AlwaysPauseGate : IHotkeyProbeSafetyGate
    {
        public HotkeyProbeSafety Check() => HotkeyProbeSafety.Pause;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
