using KeyRadar.Hotkeys;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.DeepConfirmation;

public sealed class ImmediateDeepConfirmationServiceTests
{
    [Fact]
    public async Task Occupied_target_with_only_running_rule_candidates_stays_possible()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var service = new ImmediateDeepConfirmationService(_ => Probe(target, HotkeyProbeAvailability.Occupied));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(
                target,
                [new DeepConfirmationCandidate("wechat", "微信", DeepConfirmationEvidenceKind.OfficialRule)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(DeepConfirmationConclusion.PossibleOwner, result.Conclusion);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public async Task Occupied_target_without_safe_owner_evidence_remains_unknown()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var service = new ImmediateDeepConfirmationService(_ => Probe(target, HotkeyProbeAvailability.Occupied));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(target, []),
            TestContext.Current.CancellationToken);

        Assert.Equal(DeepConfirmationConclusion.OccupiedOwnerUnknown, result.Conclusion);
    }

    [Theory]
    [InlineData(DeepConfirmationEvidenceKind.LocalConfiguration)]
    [InlineData(DeepConfirmationEvidenceKind.ActiveHardwareProfile)]
    public async Task Exact_current_evidence_confirms_owner(DeepConfirmationEvidenceKind evidence)
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var service = new ImmediateDeepConfirmationService(_ => Probe(target, HotkeyProbeAvailability.Occupied));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(
                target,
                [new DeepConfirmationCandidate("wechat", "微信", evidence)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(DeepConfirmationConclusion.ConfirmedOwner, result.Conclusion);
    }

    [Fact]
    public async Task Confirmation_refreshes_current_evidence_without_waiting_for_input()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var refreshCalls = 0;
        var service = new ImmediateDeepConfirmationService(
            _ => Probe(target, HotkeyProbeAvailability.Occupied),
            (gesture, _) =>
            {
                refreshCalls++;
                Assert.Equal(target, gesture);
                IReadOnlyList<DeepConfirmationCandidate> candidates =
                [new("wechat", "WeChat", DeepConfirmationEvidenceKind.LocalConfiguration)];
                return Task.FromResult(candidates);
            });

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(target, []),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, refreshCalls);
        Assert.Equal(DeepConfirmationConclusion.ConfirmedOwner, result.Conclusion);
        Assert.Equal(DeepConfirmationEvidenceKind.LocalConfiguration, Assert.Single(result.Candidates).Evidence);
    }

    [Fact]
    public async Task Refreshed_candidates_are_merged_without_duplicate_owners()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var existing = new DeepConfirmationCandidate("wechat", "WeChat", DeepConfirmationEvidenceKind.OfficialRule);
        var service = new ImmediateDeepConfirmationService(
            _ => Probe(target, HotkeyProbeAvailability.Occupied),
            (_, _) => Task.FromResult<IReadOnlyList<DeepConfirmationCandidate>>([existing]));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(target, [existing]),
            TestContext.Current.CancellationToken);

        Assert.Single(result.Candidates);
        Assert.Equal(DeepConfirmationConclusion.PossibleOwner, result.Conclusion);
    }

    [Fact]
    public async Task Managed_and_native_probe_disagreement_is_reported_as_unable_to_confirm()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var service = new ImmediateDeepConfirmationService(
            _ => Probe(target, HotkeyProbeAvailability.Occupied),
            nativeReprobe: (_, _) => Task.FromResult<IReadOnlyList<NativeHotkeyProbeResult>>(
                [new(NativeProbeArchitecture.X64, HotkeyProbeAvailability.AvailableAtScanTime, null)]));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(target, []),
            TestContext.Current.CancellationToken);

        Assert.Equal(DeepConfirmationConclusion.UnableToConfirm, result.Conclusion);
        Assert.Single(result.NativeProbes);
    }

    private static HotkeyProbeResult Probe(HotkeyGesture target, HotkeyProbeAvailability availability) =>
        new(target, availability, HotkeyProbeMechanism.RegisterHotKeyProbe, HotkeyOwner.Unknown, DateTimeOffset.UtcNow);
}
