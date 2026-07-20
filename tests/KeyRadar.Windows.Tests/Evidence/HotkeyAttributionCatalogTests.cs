using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class HotkeyAttributionCatalogTests
{
    [Fact]
    public void Windows_system_rule_prevents_occupied_probe_from_being_listed_as_unknown()
    {
        var target = HotkeyGesture.Parse("Alt+F4");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [new RunningRuleHotkey(
                "windows-system",
                target,
                "Close the active window or app",
                HotkeyScope.WindowsSystem,
                OwnershipConfidence.SystemKnown,
                "Microsoft documentation")],
            [],
            []);

        Assert.True(catalog.TryGet(target, out var discovered));
        Assert.Equal(HotkeyOwnershipStatus.WindowsKnown, discovered.Ownership);
        Assert.DoesNotContain(target, catalog.UnknownProbeGestures);
    }

    [Fact]
    public void Running_nvidia_rule_prevents_occupied_probe_from_being_listed_as_unknown()
    {
        var target = HotkeyGesture.Parse("Alt+Z");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [new RunningRuleHotkey(
                "nvidia-app",
                target,
                "Open NVIDIA Overlay (default hotkey, configurable)",
                HotkeyScope.Global,
                OwnershipConfidence.OfficialDefault,
                "NVIDIA documentation")],
            [],
            []);

        Assert.True(catalog.TryGet(target, out var discovered));
        Assert.Equal(HotkeyOwnershipStatus.OfficialDefault, discovered.Ownership);
        Assert.DoesNotContain(target, catalog.UnknownProbeGestures);
    }

    [Fact]
    public void Probe_stays_unknown_when_no_running_rule_or_local_evidence_exists()
    {
        var target = HotkeyGesture.Parse("Alt+Z");
        var catalog = HotkeyAttributionCatalog.Create([Probe(target)], [], [], []);

        Assert.True(catalog.TryGet(target, out var discovered));
        Assert.Equal(HotkeyOwnershipStatus.OccupiedOwnerUnknown, discovered.Ownership);
        Assert.Contains(target, catalog.UnknownProbeGestures);
    }

    [Fact]
    public void Multiple_running_rule_candidates_remain_known_but_are_marked_possible()
    {
        var target = HotkeyGesture.Parse("Ctrl+Shift+A");
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(target)],
            [
                new RunningRuleHotkey("feishu", target, "Screenshot", HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "rule"),
                new RunningRuleHotkey("dingtalk", target, "Screenshot", HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "rule"),
            ],
            [],
            []);

        Assert.True(catalog.TryGet(target, out var discovered));
        Assert.Equal(HotkeyOwnershipStatus.PossibleOwner, discovered.Ownership);
        Assert.DoesNotContain(target, catalog.UnknownProbeGestures);
    }

    private static HotkeyProbeResult Probe(HotkeyGesture gesture) => new(
        gesture,
        HotkeyProbeAvailability.Occupied,
        HotkeyProbeMechanism.RegisterHotKeyProbe,
        HotkeyOwner.Unknown,
        DateTimeOffset.UtcNow);
}
