using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Configuration;
using KeyRadar.Windows.Evidence;

namespace KeyRadar.Windows.Tests.Configuration;

public sealed class LocalConfigurationOverridePolicyTests
{
    [Fact]
    public void Local_command_mapping_replaces_only_the_matching_official_default()
    {
        var rules = new[]
        {
            new RunningRuleHotkey("nvidia-app", HotkeyGesture.Parse("Alt+R"), "Performance", HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "official", "42:nvidia-app:overlay", "overlay", "performance-overlay-toggle"),
            new RunningRuleHotkey("nvidia-app", HotkeyGesture.Parse("Alt+Z"), "Overlay", HotkeyScope.Global, OwnershipConfidence.OfficialDefault, "official", "42:nvidia-app:overlay", "overlay", "open-overlay"),
        };
        var local = new[]
        {
            new LocalConfigurationHotkey("nvidia-app", HotkeyGesture.Parse("Alt+I"), "Performance", HotkeyScope.Global, "config", "42:nvidia-app:overlay", "overlay", "performance-overlay-toggle"),
        };

        var filtered = LocalConfigurationOverridePolicy.FilterStaticDefaults(rules, local);

        Assert.DoesNotContain(filtered, rule => rule.Gesture == HotkeyGesture.Parse("Alt+R"));
        Assert.Contains(filtered, rule => rule.Gesture == HotkeyGesture.Parse("Alt+Z"));
    }

    [Fact]
    public void Local_command_mapping_replaces_matching_suspected_default_without_removing_other_evidence()
    {
        var rules = new[]
        {
            new RunningRuleHotkey("sharex", HotkeyGesture.Parse("PrintScreen"), "Screen", HotkeyScope.Global, OwnershipConfidence.Suspected, "static", "42:sharex:default", "default", "capture-screen"),
            new RunningRuleHotkey("sharex", HotkeyGesture.Parse("Ctrl+PrintScreen"), "Region", HotkeyScope.Global, OwnershipConfidence.Suspected, "static", "42:sharex:default", "default", "capture-region"),
            new RunningRuleHotkey("other", HotkeyGesture.Parse("PrintScreen"), "Other", HotkeyScope.Global, OwnershipConfidence.Suspected, "static", "43:other:default", "default", "capture-screen"),
            new RunningRuleHotkey("sharex", HotkeyGesture.Parse("Alt+PrintScreen"), "Declared", HotkeyScope.Global, OwnershipConfidence.UserDeclared, "user", "42:sharex:default", "default", "capture-screen"),
        };
        var local = new[]
        {
            new LocalConfigurationHotkey("sharex", HotkeyGesture.Parse("F13"), "Screen", HotkeyScope.Global, "config", "42:sharex:default", "default", "capture-screen"),
        };

        var filtered = LocalConfigurationOverridePolicy.FilterStaticDefaults(rules, local);

        Assert.DoesNotContain(filtered, rule => rule.ApplicationId == "sharex" && rule.CommandId == "capture-screen" && rule.Confidence == OwnershipConfidence.Suspected);
        Assert.Contains(filtered, rule => rule.CommandId == "capture-region");
        Assert.Contains(filtered, rule => rule.ApplicationId == "other");
        Assert.Contains(filtered, rule => rule.Confidence == OwnershipConfidence.UserDeclared);
    }
}
