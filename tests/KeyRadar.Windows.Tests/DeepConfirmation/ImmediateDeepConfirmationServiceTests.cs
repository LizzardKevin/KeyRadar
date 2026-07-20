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

    private static HotkeyProbeResult Probe(HotkeyGesture target, HotkeyProbeAvailability availability) =>
        new(target, availability, HotkeyProbeMechanism.RegisterHotKeyProbe, HotkeyOwner.Unknown, DateTimeOffset.UtcNow);
}
