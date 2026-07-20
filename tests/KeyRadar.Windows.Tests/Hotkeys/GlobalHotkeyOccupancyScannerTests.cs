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

        var results = await scanner.ScanAsync(
            [HotkeyGesture.Parse("Alt+A")],
            progress: null,
            TestContext.Current.CancellationToken);

        Assert.Empty(results);
        Assert.Equal(0, api.RegisterCalls);
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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
