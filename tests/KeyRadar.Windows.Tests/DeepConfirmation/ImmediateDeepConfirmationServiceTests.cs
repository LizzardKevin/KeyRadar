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
    public async Task Refreshed_exact_evidence_confirms_owner(DeepConfirmationEvidenceKind evidence)
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var exact = new DeepConfirmationCandidate("wechat", "WeChat", evidence);
        var service = new ImmediateDeepConfirmationService(
            _ => Probe(target, HotkeyProbeAvailability.Occupied),
            (_, _) => Task.FromResult<IReadOnlyList<DeepConfirmationCandidate>>([exact]));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(
                target,
                [new DeepConfirmationCandidate("wechat", "微信", evidence)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(DeepConfirmationConclusion.ConfirmedOwner, result.Conclusion);
    }

    [Fact]
    public async Task Refreshed_exact_evidence_does_not_confirm_when_managed_reprobe_is_available()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var exact = new DeepConfirmationCandidate(
            "wechat",
            "WeChat",
            DeepConfirmationEvidenceKind.LocalConfiguration);
        var service = new ImmediateDeepConfirmationService(
            _ => Probe(target, HotkeyProbeAvailability.AvailableAtScanTime),
            (_, _) => Task.FromResult<IReadOnlyList<DeepConfirmationCandidate>>([exact]));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(target, []),
            TestContext.Current.CancellationToken);

        Assert.Equal(DeepConfirmationConclusion.UnableToConfirm, result.Conclusion);
        Assert.Equal(exact, Assert.Single(result.Candidates));
    }

    [Fact]
    public async Task Refreshed_exact_evidence_does_not_confirm_when_native_reprobe_disagrees()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var exact = new DeepConfirmationCandidate(
            "wechat",
            "WeChat",
            DeepConfirmationEvidenceKind.LocalConfiguration);
        var service = new ImmediateDeepConfirmationService(
            _ => Probe(target, HotkeyProbeAvailability.Occupied),
            (_, _) => Task.FromResult<IReadOnlyList<DeepConfirmationCandidate>>([exact]),
            (_, _) => Task.FromResult<IReadOnlyList<NativeHotkeyProbeResult>>(
                [new(NativeProbeArchitecture.X86, HotkeyProbeAvailability.AvailableAtScanTime, null)]));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(target, []),
            TestContext.Current.CancellationToken);

        Assert.Equal(DeepConfirmationConclusion.UnableToConfirm, result.Conclusion);
        Assert.Equal(exact, Assert.Single(result.Candidates));
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

    [Theory]
    [InlineData(DeepConfirmationEvidenceKind.LocalConfiguration)]
    [InlineData(DeepConfirmationEvidenceKind.ActiveHardwareProfile)]
    public async Task Successful_empty_refresh_discards_stale_exact_evidence(DeepConfirmationEvidenceKind evidence)
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var service = new ImmediateDeepConfirmationService(
            _ => Probe(target, HotkeyProbeAvailability.Occupied),
            (_, _) => Task.FromResult<IReadOnlyList<DeepConfirmationCandidate>>([]));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(
                target,
                [new DeepConfirmationCandidate("wechat", "WeChat", evidence)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(DeepConfirmationConclusion.OccupiedOwnerUnknown, result.Conclusion);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Failed_refresh_cannot_confirm_stale_exact_evidence()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var service = new ImmediateDeepConfirmationService(
            _ => Probe(target, HotkeyProbeAvailability.Occupied),
            (_, _) => Task.FromException<IReadOnlyList<DeepConfirmationCandidate>>(new InvalidOperationException("refresh failed")));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(
                target,
                [new DeepConfirmationCandidate("wechat", "WeChat", DeepConfirmationEvidenceKind.LocalConfiguration)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(DeepConfirmationConclusion.UnableToConfirm, result.Conclusion);
    }

    [Fact]
    public async Task Refreshed_possible_candidates_remain_possible()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var possible = new DeepConfirmationCandidate("wechat", "WeChat", DeepConfirmationEvidenceKind.OfficialRule);
        var service = new ImmediateDeepConfirmationService(
            _ => Probe(target, HotkeyProbeAvailability.Occupied),
            (_, _) => Task.FromResult<IReadOnlyList<DeepConfirmationCandidate>>([possible]));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(target, []),
            TestContext.Current.CancellationToken);

        Assert.Equal(DeepConfirmationConclusion.PossibleOwner, result.Conclusion);
        Assert.Equal(possible, Assert.Single(result.Candidates));
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
    public async Task Refreshed_candidates_are_canonicalized_independently_of_scanner_order()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var service = new ImmediateDeepConfirmationService(
            _ => Probe(target, HotkeyProbeAvailability.Occupied),
            (_, _) => Task.FromResult<IReadOnlyList<DeepConfirmationCandidate>>(
            [
                new("zulu", "Zulu", DeepConfirmationEvidenceKind.OfficialRule, "202\u001Fzulu\u001Fstable"),
                new("beta", "Beta", DeepConfirmationEvidenceKind.ActiveHardwareProfile),
                new("alpha", "Alpha", DeepConfirmationEvidenceKind.LocalConfiguration, "101\u001Falpha\u001Fstable"),
            ]));

        var result = await service.ConfirmAsync(
            new ImmediateDeepConfirmationRequest(target, []),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["alpha", "beta"],
            result.Candidates.Select(candidate => candidate.OwnerId));
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
