using KeyRadar.Hotkeys;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.DeepConfirmation;

public sealed class HotkeyOwnerTracePolicyTests
{
    [Fact]
    public async Task TraceEligibleAsync_runs_curated_unknown_targets_serially_and_keeps_boundary_results()
    {
        var catalog = HotkeyAttributionCatalog.Create(
        [
            Probe("Ctrl+Alt+K", HotkeyProbeAvailability.Occupied),
            Probe("Ctrl+Alt+L", HotkeyProbeAvailability.Occupied),
            Probe("F12", HotkeyProbeAvailability.Occupied),
        ], [], [], []);
        var tracer = new RecordingTracer(
            new OwnerTraceResult(OwnerTraceStatus.Detected, 42, 7, "recipient"),
            new OwnerTraceResult(OwnerTraceStatus.NotDetected));
        var coordinator = new HotkeyOwnerTraceCoordinator(tracer, TimeSpan.FromSeconds(2));

        var results = await coordinator.TraceEligibleAsync(catalog, CancellationToken.None);

        Assert.Equal(["Ctrl+Alt+K", "Ctrl+Alt+L"], tracer.Targets.Select(target => target.Gesture.ToString()));
        Assert.Equal(OwnerTraceStatus.Detected, results[HotkeyGesture.Parse("Ctrl+Alt+K")].Status);
        Assert.Equal(OwnerTraceStatus.NotDetected, results[HotkeyGesture.Parse("Ctrl+Alt+L")].Status);
        Assert.False(results.ContainsKey(HotkeyGesture.Parse("F12")));
    }

    [Fact]
    public void CreateTargets_includes_only_curated_occupied_unknown_hotkeys()
    {
        var probes = new[]
        {
            Probe("Ctrl+Alt+K", HotkeyProbeAvailability.Occupied),
            Probe("F12", HotkeyProbeAvailability.Occupied),
            Probe("Ctrl+Alt+L", HotkeyProbeAvailability.AvailableAtScanTime),
        };
        var catalog = HotkeyAttributionCatalog.Create(probes, [], [], []);

        var targets = HotkeyOwnerTracePolicy.CreateTargets(catalog);

        var target = Assert.Single(targets);
        Assert.Equal(HotkeyGesture.Parse("Ctrl+Alt+K"), target.Gesture);
    }

    [Theory]
    [InlineData("Win+K")]
    [InlineData("Win+Ctrl+K")]
    [InlineData("Alt+F4")]
    [InlineData("Alt+Tab")]
    [InlineData("Alt+Esc")]
    [InlineData("Alt+Space")]
    [InlineData("Ctrl+Esc")]
    [InlineData("Ctrl+Shift+Esc")]
    [InlineData("PrintScreen")]
    [InlineData("Ctrl+PrintScreen")]
    [InlineData("Ctrl+Alt+Delete")]
    public void CreateTargets_never_selects_a_gesture_that_must_not_be_sent_as_input(string gesture)
    {
        var catalog = HotkeyAttributionCatalog.Create(
        [
            Probe("Ctrl+Alt+K", HotkeyProbeAvailability.Occupied),
            Probe(gesture, HotkeyProbeAvailability.Occupied),
        ], [], [], []);

        var targets = HotkeyOwnerTracePolicy.CreateTargets(catalog);

        var target = Assert.Single(targets);
        Assert.Equal(HotkeyGesture.Parse("Ctrl+Alt+K"), target.Gesture);
    }

    [Fact]
    public void CreateTargets_excludes_already_attributed_and_windows_owned_hotkeys()
    {
        var probes = new[]
        {
            Probe("Ctrl+Alt+K", HotkeyProbeAvailability.Occupied),
            Probe("Ctrl+Alt+W", HotkeyProbeAvailability.Occupied),
        };
        var rules = new[]
        {
            new RunningRuleHotkey("known-app", HotkeyGesture.Parse("Ctrl+Alt+K"), "Known", KeyRadar.Conflicts.HotkeyScope.Global, KeyRadar.Conflicts.OwnershipConfidence.Confirmed, "test"),
            new RunningRuleHotkey("windows-system", HotkeyGesture.Parse("Ctrl+Alt+W"), "Windows", KeyRadar.Conflicts.HotkeyScope.WindowsSystem, KeyRadar.Conflicts.OwnershipConfidence.SystemKnown, "test"),
        };
        var catalog = HotkeyAttributionCatalog.Create(probes, rules, [], []);

        Assert.Empty(HotkeyOwnerTracePolicy.CreateTargets(catalog));
    }

    [Fact]
    public async Task TraceEligibleAsync_enforces_the_target_budget()
    {
        var catalog = HotkeyAttributionCatalog.Create(
        [
            Probe("Ctrl+Alt+K", HotkeyProbeAvailability.Occupied),
            Probe("Ctrl+Alt+L", HotkeyProbeAvailability.Occupied),
            Probe("Ctrl+Alt+M", HotkeyProbeAvailability.Occupied),
        ], [], [], []);
        var tracer = new RecordingTracer(
            new OwnerTraceResult(OwnerTraceStatus.NotDetected),
            new OwnerTraceResult(OwnerTraceStatus.NotDetected));
        var coordinator = new HotkeyOwnerTraceCoordinator(
            tracer,
            totalBudget: TimeSpan.FromSeconds(1),
            perTargetBudget: TimeSpan.FromMilliseconds(100),
            maximumTargets: 2);

        var results = await coordinator.TraceEligibleAsync(catalog, CancellationToken.None);

        Assert.Equal(2, tracer.Targets.Count);
        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(HotkeyGesture.Parse("Ctrl+Alt+M"), results.Keys);
    }

    [Fact]
    public async Task TraceEligibleAsync_marks_a_per_target_timeout_without_assigning_an_owner()
    {
        var catalog = HotkeyAttributionCatalog.Create(
        [Probe("Ctrl+Alt+K", HotkeyProbeAvailability.Occupied)], [], [], []);
        var coordinator = new HotkeyOwnerTraceCoordinator(
            new WaitForCancellationTracer(),
            totalBudget: TimeSpan.FromSeconds(1),
            perTargetBudget: TimeSpan.FromMilliseconds(25),
            maximumTargets: 1);

        var results = await coordinator.TraceEligibleAsync(catalog, CancellationToken.None);

        var result = Assert.Single(results).Value;
        Assert.Equal(OwnerTraceStatus.TimedOut, result.Status);
        Assert.Null(result.ProcessId);
        Assert.Null(result.ThreadId);
    }

    private static HotkeyProbeResult Probe(string gesture, HotkeyProbeAvailability availability) => new(
        HotkeyGesture.Parse(gesture), availability, HotkeyProbeMechanism.RegisterHotKeyProbe,
        HotkeyOwner.Unknown, DateTimeOffset.UtcNow, null);

    private sealed class RecordingTracer(params OwnerTraceResult[] results) : IHotkeyOwnerTracer
    {
        private readonly Queue<OwnerTraceResult> _results = new(results);

        public List<HotkeyOwnerTraceTarget> Targets { get; } = [];

        public Task<OwnerTraceResult> TraceAsync(HotkeyOwnerTraceTarget target, CancellationToken cancellationToken)
        {
            Targets.Add(target);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class WaitForCancellationTracer : IHotkeyOwnerTracer
    {
        public async Task<OwnerTraceResult> TraceAsync(HotkeyOwnerTraceTarget target, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new OwnerTraceResult(OwnerTraceStatus.Failed);
        }
    }
}
