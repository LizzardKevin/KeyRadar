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

    private static HotkeyProbeResult Probe(HotkeyGesture gesture) => new(
        gesture,
        HotkeyProbeAvailability.Occupied,
        HotkeyProbeMechanism.RegisterHotKeyProbe,
        HotkeyOwner.Unknown,
        DateTimeOffset.UtcNow);

    private static RunningRuleHotkey RuleCandidate(string owner, HotkeyGesture gesture, string function) =>
        new(owner, gesture, function, HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "官方规则");
}
