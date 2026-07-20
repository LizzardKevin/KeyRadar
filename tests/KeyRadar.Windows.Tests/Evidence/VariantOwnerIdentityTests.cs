using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class VariantOwnerIdentityTests
{
    [Fact]
    public void Foreground_variant_identity_selects_its_own_group_regardless_of_input_order()
    {
        var gesture = HotkeyGesture.Parse("F6");
        var foreground = new RunningApplicationEvidenceIdentity(101, "browser", "modern");
        var background = new RunningApplicationEvidenceIdentity(202, "browser", "legacy");
        var foregroundRule = Rule(foreground, gesture, HotkeyScope.Foreground);
        var owners = new[]
        {
            Owner(foreground, HotkeyInventoryGroup.ForegroundApplication),
            Owner(background, HotkeyInventoryGroup.BackgroundApplication),
        };

        var first = Project([foregroundRule], owners, gesture);
        var reversed = Project([foregroundRule], owners.Reverse().ToArray(), gesture);

        Assert.Equal("variant-uncertain-101", first.PrimaryGroupId);
        Assert.Equal(HotkeyInventoryGroup.ForegroundApplication, first.Group);
        Assert.Equal(first.PrimaryGroupId, reversed.PrimaryGroupId);
        Assert.Equal(first.Group, reversed.Group);
        Assert.Single(first.DeepConfirmationCandidates);
        Assert.Equal("browser", first.DeepConfirmationCandidates[0].OwnerId);
    }

    [Fact]
    public void Per_process_eligibility_excludes_background_in_app_rule_without_dropping_background_global_rule()
    {
        var foreground = new RunningApplicationEvidenceIdentity(101, "browser", "modern");
        var background = new RunningApplicationEvidenceIdentity(202, "browser", "legacy");
        var f6 = Rule(foreground, HotkeyGesture.Parse("F6"), HotkeyScope.Foreground);
        var altZ = Rule(background, HotkeyGesture.Parse("Alt+Z"), HotkeyScope.Global);
        var presence = new Dictionary<string, ApplicationPresence>
        {
            [foreground.Value] = ApplicationPresence.Foreground,
            [background.Value] = ApplicationPresence.Background,
        };

        var eligible = CurrentStateHotkeyEligibilityPolicy.FilterRunningRulesByOwnerIdentity([f6, altZ], presence);

        Assert.Equal(["Alt+Z", "F6"], eligible.Select(rule => rule.Gesture.ToString()).OrderBy(value => value));
        var rows = NormalHotkeyInventoryProjector.Project(
            HotkeyAttributionCatalog.Create(
                [Probe(HotkeyGesture.Parse("F6")), Probe(HotkeyGesture.Parse("Alt+Z"))],
                eligible,
                [],
                []).Items,
            [
                Owner(foreground, HotkeyInventoryGroup.ForegroundApplication),
                Owner(background, HotkeyInventoryGroup.BackgroundApplication),
            ]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(HotkeyInventoryGroup.ForegroundApplication, Assert.Single(rows, row => row.Item.Gesture.ToString() == "F6").Group);
        Assert.Equal(HotkeyInventoryGroup.BackgroundApplication, Assert.Single(rows, row => row.Item.Gesture.ToString() == "Alt+Z").Group);
        Assert.DoesNotContain(rows, row =>
            row.Group == HotkeyInventoryGroup.BackgroundApplication && row.Item.Gesture.ToString() == "F6");
    }

    [Fact]
    public void Variant_rule_merge_is_deterministic_and_keeps_all_variant_candidates()
    {
        var gesture = HotkeyGesture.Parse("Alt+Z");
        var modern = new RunningApplicationEvidenceIdentity(101, "browser", "modern");
        var legacy = new RunningApplicationEvidenceIdentity(101, "browser", "legacy");
        var global = Rule(modern, gesture, HotkeyScope.Global) with { Function = "Open overlay" };
        var background = Rule(legacy, gesture, HotkeyScope.Background) with { Function = "Open menu" };
        var owners = new[]
        {
            Owner(modern, HotkeyInventoryGroup.ForegroundApplication),
            Owner(legacy, HotkeyInventoryGroup.ForegroundApplication),
        };

        var first = Project([global, background], owners, gesture);
        var reversed = Project([background, global], owners.Reverse().ToArray(), gesture);

        Assert.Equal(HotkeyOwnershipStatus.OfficialDefault, first.Item.Ownership);
        Assert.Equal(HotkeyConflictStatus.None, first.Item.Conflict);
        Assert.Equal("Open overlay", first.Item.Function);
        Assert.Equal(HotkeyScope.Global, first.Item.Scope);
        Assert.Equal(first.Item.Function, reversed.Item.Function);
        Assert.Equal(first.Item.Scope, reversed.Item.Scope);
        Assert.Equal(2, first.DeepConfirmationCandidates.Count);
        Assert.Equal(
            [legacy.Value, modern.Value],
            first.DeepConfirmationCandidates.Select(candidate => candidate.EvidenceIdentity));
    }

    [Fact]
    public void Reversed_ambiguous_rules_keep_deep_confirmation_candidates_in_stable_evidence_and_owner_order()
    {
        var gesture = HotkeyGesture.Parse("Ctrl+Alt+R");
        var alpha = new RunningApplicationEvidenceIdentity(101, "alpha-app", "modern");
        var zulu = new RunningApplicationEvidenceIdentity(202, "zulu-app", "legacy");
        var alphaRule = Rule(alpha, gesture, HotkeyScope.Global);
        var zuluRule = Rule(zulu, gesture, HotkeyScope.Global);
        var alphaOwner = Owner(alpha, HotkeyInventoryGroup.ForegroundApplication) with
        {
            Evidence = DeepConfirmationEvidenceKind.LocalConfiguration,
            DisplayName = "Alpha",
        };
        var zuluOwner = Owner(zulu, HotkeyInventoryGroup.ForegroundApplication) with
        {
            Evidence = DeepConfirmationEvidenceKind.ActiveHardwareProfile,
            DisplayName = "Zulu",
        };

        var first = Project([zuluRule, alphaRule], [zuluOwner, alphaOwner], gesture);
        var reversed = Project([alphaRule, zuluRule], [alphaOwner, zuluOwner], gesture);

        Assert.Equal(first.DeepConfirmationCandidates, reversed.DeepConfirmationCandidates);
        Assert.Equal(["alpha-app", "zulu-app"], first.DeepConfirmationCandidates.Select(candidate => candidate.OwnerId));
        Assert.Equal(
            [DeepConfirmationEvidenceKind.LocalConfiguration, DeepConfirmationEvidenceKind.ActiveHardwareProfile],
            first.DeepConfirmationCandidates.Select(candidate => candidate.Evidence));
    }

    private static ProjectedHotkeyInventoryRow Project(
        IReadOnlyList<RunningRuleHotkey> rules,
        IReadOnlyList<HotkeyInventoryOwner> owners,
        HotkeyGesture gesture) => Assert.Single(NormalHotkeyInventoryProjector.Project(
            HotkeyAttributionCatalog.Create([Probe(gesture)], rules, [], []).Items,
            owners));

    private static RunningRuleHotkey Rule(
        RunningApplicationEvidenceIdentity identity,
        HotkeyGesture gesture,
        HotkeyScope scope) => new(
            identity.ApplicationId,
            gesture,
            "Shortcut",
            scope,
            OwnershipConfidence.OfficialDefault,
            "rule",
            OwnerIdentity: identity.Value,
            VariantId: identity.VariantId);

    private static HotkeyInventoryOwner Owner(
        RunningApplicationEvidenceIdentity identity,
        HotkeyInventoryGroup group) => new(
            identity.Value,
            group,
            $"variant-uncertain-{identity.ProcessId}",
            "Browser",
            DeepConfirmationEvidenceKind.OfficialRule,
            DisplayOwnerId: identity.ApplicationId);

    private static HotkeyProbeResult Probe(HotkeyGesture gesture) => new(
        gesture,
        HotkeyProbeAvailability.Occupied,
        HotkeyProbeMechanism.RegisterHotKeyProbe,
        HotkeyOwner.Unknown,
        DateTimeOffset.UtcNow);
}
