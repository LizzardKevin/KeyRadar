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

        var filtered = LocalConfigurationOverridePolicy.FilterOfficialDefaults(rules, local);

        Assert.DoesNotContain(filtered, rule => rule.Gesture == HotkeyGesture.Parse("Alt+R"));
        Assert.Contains(filtered, rule => rule.Gesture == HotkeyGesture.Parse("Alt+Z"));
    }
}
