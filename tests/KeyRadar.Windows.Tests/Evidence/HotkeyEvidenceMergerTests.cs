using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Windows.Configuration;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hardware;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class HotkeyEvidenceMergerTests
{
    [Fact]
    public void Occupied_probe_without_owner_is_retained_as_unknown()
    {
        var target = HotkeyGesture.Parse("Alt+A");

        var result = HotkeyEvidenceMerger.Merge([Probe(target)], [], [], []);

        var item = Assert.Single(result);
        Assert.Equal(HotkeyOwnershipStatus.OccupiedOwnerUnknown, item.Ownership);
        Assert.Empty(item.Owners);
    }

    [Fact]
    public void Local_configuration_beats_the_official_default()
    {
        var target = HotkeyGesture.Parse("Ctrl+L");
        var local = new LocalConfigurationHotkey("wechat", target, "锁定微信", HotkeyScope.Global, "微信本机配置");

        var result = HotkeyEvidenceMerger.Merge(
            [Probe(target)],
            [local],
            [],
            [RuleCandidate("wechat", target, "官方默认锁定")]);

        var item = Assert.Single(result);
        Assert.Equal("锁定微信", item.Function);
        Assert.Equal(HotkeyOwnershipStatus.LocalConfigurationFound, item.Ownership);
        Assert.Equal("wechat", Assert.Single(item.Owners));
    }

    [Fact]
    public void Active_hardware_mapping_collides_with_an_application_target()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var local = new LocalConfigurationHotkey("wechat", target, "截图", HotkeyScope.Global, "微信本机配置");
        var profile = new HardwareProfileDescriptor(
            "logitech-g-hub", "Logitech G Keyboard", "Desktop Profile",
            HardwareProfileReadStatus.Active, false, null,
            [new HardwareMapping("G2", HardwareMappingTargetKind.Hotkey, target, "Alt+A", true)],
            "G HUB Desktop Profile 当前生效");

        var result = HotkeyEvidenceMerger.Merge([Probe(target)], [local], [profile], []);

        Assert.Equal(HotkeyConflictStatus.HardwareMappingCollision, Assert.Single(result).Conflict);
    }

    [Fact]
    public void Two_running_rule_candidates_are_a_possible_interception_not_a_definite_conflict()
    {
        var target = HotkeyGesture.Parse("Ctrl+Shift+A");

        var result = HotkeyEvidenceMerger.Merge(
            [Probe(target)],
            [],
            [],
            [RuleCandidate("feishu", target, "Screenshot"), RuleCandidate("dingtalk", target, "Screenshot")]);

        var item = Assert.Single(result);
        Assert.Equal(HotkeyOwnershipStatus.PossibleOwner, item.Ownership);
        Assert.Equal(HotkeyConflictStatus.PossibleInterception, item.Conflict);
    }

    [Fact]
    public void Same_application_and_variant_across_processes_are_one_logical_rule_owner()
    {
        var target = HotkeyGesture.Parse("Ctrl+Shift+A");
        var first = new RunningApplicationEvidenceIdentity(101, "sharex", "stable");
        var second = new RunningApplicationEvidenceIdentity(202, "sharex", "stable");

        var item = Assert.Single(HotkeyEvidenceMerger.Merge(
            [Probe(target)],
            [],
            [],
            [
                RuleCandidate("sharex", target, "Capture") with { OwnerIdentity = first.Value, VariantId = first.VariantId },
                RuleCandidate("sharex", target, "Capture") with { OwnerIdentity = second.Value, VariantId = second.VariantId },
            ]));

        Assert.Equal(HotkeyOwnershipStatus.OfficialDefault, item.Ownership);
        Assert.Equal(HotkeyConflictStatus.None, item.Conflict);
        Assert.Equal(["sharex"], item.Owners);
    }

    [Fact]
    public void Same_application_local_configuration_across_processes_is_one_logical_owner()
    {
        var target = HotkeyGesture.Parse("Ctrl+Shift+A");
        var first = new RunningApplicationEvidenceIdentity(101, "sharex", "stable");
        var second = new RunningApplicationEvidenceIdentity(202, "sharex", "stable");

        var item = Assert.Single(HotkeyEvidenceMerger.Merge(
            [Probe(target)],
            [
                new LocalConfigurationHotkey("sharex", target, "Capture", HotkeyScope.Global, "first", first.Value, first.VariantId),
                new LocalConfigurationHotkey("sharex", target, "Capture", HotkeyScope.Global, "second", second.Value, second.VariantId),
            ],
            [],
            []));

        Assert.Equal(HotkeyOwnershipStatus.LocalConfigurationFound, item.Ownership);
        Assert.Equal(HotkeyConflictStatus.None, item.Conflict);
        Assert.Equal(["sharex"], item.Owners);
    }

    [Fact]
    public void Different_variants_of_one_application_are_not_cross_application_conflicts()
    {
        var target = HotkeyGesture.Parse("Ctrl+Shift+A");
        var modern = new RunningApplicationEvidenceIdentity(101, "browser", "modern");
        var legacy = new RunningApplicationEvidenceIdentity(101, "browser", "legacy");

        var item = Assert.Single(HotkeyEvidenceMerger.Merge(
            [Probe(target)],
            [],
            [],
            [
                RuleCandidate("browser", target, "Capture") with { OwnerIdentity = modern.Value, VariantId = modern.VariantId },
                RuleCandidate("browser", target, "Capture") with { OwnerIdentity = legacy.Value, VariantId = legacy.VariantId },
            ]));

        Assert.Equal(HotkeyOwnershipStatus.OfficialDefault, item.Ownership);
        Assert.Equal(HotkeyConflictStatus.None, item.Conflict);
    }

    [Fact]
    public void A_local_owner_and_a_different_rule_candidate_are_a_possible_interception()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var local = new LocalConfigurationHotkey("wechat", target, "Screenshot", HotkeyScope.Global, "Local config");

        var result = HotkeyEvidenceMerger.Merge(
            [Probe(target)],
            [local],
            [],
            [RuleCandidate("other-app", target, "Capture")]);

        Assert.Equal(HotkeyConflictStatus.PossibleInterception, Assert.Single(result).Conflict);
    }

    [Fact]
    public void An_inactive_hardware_profile_does_not_participate_in_conflicts()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var profile = new HardwareProfileDescriptor(
            "logitech-g-hub", "Logitech G Keyboard", "Photoshop Profile",
            HardwareProfileReadStatus.Inactive, false, null,
            [new HardwareMapping("G2", HardwareMappingTargetKind.Hotkey, target, "Alt+A", true)],
            "Inactive profile");

        var result = HotkeyEvidenceMerger.Merge(
            [Probe(target)],
            [],
            [profile],
            [RuleCandidate("wechat", target, "Screenshot")]);

        var item = Assert.Single(result);
        Assert.DoesNotContain("logitech-g-hub", item.Owners);
        Assert.Equal(HotkeyConflictStatus.None, item.Conflict);
    }

    [Fact]
    public void A_user_imported_current_profile_is_possible_evidence_not_a_definite_collision()
    {
        var target = HotkeyGesture.Parse("Alt+A");
        var profile = new HardwareProfileDescriptor(
            "logitech-g-hub", "Logitech G Keyboard", "Desktop Profile",
            HardwareProfileReadStatus.Active, false, null,
            [new HardwareMapping("G2", HardwareMappingTargetKind.Hotkey, target, "Alt+A", true)],
            "User-imported profile",
            IsUserDeclared: true);

        var result = HotkeyEvidenceMerger.Merge(
            [Probe(target)],
            [],
            [profile],
            [RuleCandidate("wechat", target, "Screenshot")]);

        var item = Assert.Single(result);
        Assert.Equal(HotkeyConflictStatus.PossibleInterception, item.Conflict);
        Assert.Equal(HotkeyOwnershipStatus.PossibleOwner, item.Ownership);
    }

    [Fact]
    public void Windows_system_knowledge_overrides_an_unknown_occupied_probe()
    {
        var target = HotkeyGesture.Parse("Alt+F4");

        var result = HotkeyEvidenceMerger.Merge(
            [Probe(target)],
            [],
            [],
            [new RunningRuleHotkey(
                "windows-system",
                target,
                "Close the active window or app",
                HotkeyScope.WindowsSystem,
                OwnershipConfidence.SystemKnown,
                "Microsoft documentation")]);

        var item = Assert.Single(result);
        Assert.Equal(HotkeyProbeAvailability.Occupied, item.Availability);
        Assert.Equal(HotkeyOwnershipStatus.WindowsKnown, item.Ownership);
        Assert.Equal("windows-system", Assert.Single(item.Owners));
    }

    [Fact]
    public void One_running_nvidia_overlay_rule_overrides_an_unknown_occupied_probe()
    {
        var target = HotkeyGesture.Parse("Alt+Z");

        var result = HotkeyEvidenceMerger.Merge(
            [Probe(target)],
            [],
            [],
            [new RunningRuleHotkey(
                "nvidia-app",
                target,
                "Open NVIDIA Overlay (default hotkey, configurable)",
                HotkeyScope.Global,
                OwnershipConfidence.OfficialDefault,
                "NVIDIA documentation")]);

        var item = Assert.Single(result);
        Assert.Equal(HotkeyOwnershipStatus.OfficialDefault, item.Ownership);
        Assert.Equal("nvidia-app", Assert.Single(item.Owners));
    }

    [Fact]
    public void Reversed_running_rules_keep_owner_and_evidence_order_with_probe_evidence_last()
    {
        var target = HotkeyGesture.Parse("Alt+Shift+R");
        var alpha = new RunningRuleHotkey(
            "alpha-app", target, "Capture", HotkeyScope.Global,
            OwnershipConfidence.OfficialDefault, "Alpha rule evidence");
        var zulu = new RunningRuleHotkey(
            "zulu-app", target, "Capture", HotkeyScope.Global,
            OwnershipConfidence.OfficialDefault, "Zulu rule evidence");

        var first = Assert.Single(HotkeyEvidenceMerger.Merge([Probe(target)], [], [], [zulu, alpha]));
        var reversed = Assert.Single(HotkeyEvidenceMerger.Merge([Probe(target)], [], [], [alpha, zulu]));

        Assert.Equal(first.Owners, reversed.Owners);
        Assert.Equal(first.Evidence, reversed.Evidence);
        Assert.Equal(["alpha-app", "zulu-app"], first.Owners);
        Assert.Equal(["Alpha rule evidence", "Zulu rule evidence"], first.Evidence.Take(2));
        Assert.Contains("RegisterHotKey", first.Evidence.Last());
    }

    private static HotkeyProbeResult Probe(HotkeyGesture gesture) => new(
        gesture,
        HotkeyProbeAvailability.Occupied,
        HotkeyProbeMechanism.RegisterHotKeyProbe,
        HotkeyOwner.Unknown,
        DateTimeOffset.UtcNow);

    private static RunningRuleHotkey RuleCandidate(string owner, HotkeyGesture gesture, string function) =>
        new(owner, gesture, function, HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "官方规则");
}
