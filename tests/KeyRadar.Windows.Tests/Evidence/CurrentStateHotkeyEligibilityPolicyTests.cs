using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Configuration;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class CurrentStateHotkeyEligibilityPolicyTests
{
    [Fact]
    public void Background_application_excludes_in_app_function_key()
    {
        Assert.False(CurrentStateHotkeyEligibilityPolicy.IsEligible(
            ApplicationPresence.Background,
            HotkeyScope.Foreground));
    }

    [Theory]
    [InlineData("Alt+Z")]
    [InlineData("Alt+A")]
    public void Background_application_retains_global_shortcuts(string gesture)
    {
        var rules = CurrentStateHotkeyEligibilityPolicy.FilterRunningRules(
            [Rule("chrome", gesture, HotkeyScope.Global)],
            Presence("chrome", ApplicationPresence.Background));

        Assert.Equal(gesture, Assert.Single(rules).Gesture.ToString());
    }

    [Fact]
    public void Foreground_application_retains_in_app_function_key()
    {
        Assert.True(CurrentStateHotkeyEligibilityPolicy.IsEligible(
            ApplicationPresence.Foreground,
            HotkeyScope.Foreground));
    }

    [Fact]
    public void Application_paths_do_not_treat_windows_system_rules_as_eligible()
    {
        Assert.False(CurrentStateHotkeyEligibilityPolicy.IsEligible(
            ApplicationPresence.Foreground,
            HotkeyScope.WindowsSystem));
    }

    [Fact]
    public void Windows_system_scope_is_not_filtered_as_an_application_rule()
    {
        Assert.True(CurrentStateHotkeyEligibilityPolicy.IsWindowsSystemEligible(HotkeyScope.WindowsSystem));
    }

    [Fact]
    public void Background_ambiguous_in_app_rule_is_absent_from_inventory_and_deep_confirmation_candidates()
    {
        var f6 = HotkeyGesture.Parse("F6");
        var altZ = HotkeyGesture.Parse("Alt+Z");
        var presence = Presence("chrome-ambiguous", ApplicationPresence.Background);
        var rules = CurrentStateHotkeyEligibilityPolicy.FilterRunningRules(
            [
                Rule("chrome-ambiguous", "F6", HotkeyScope.Foreground),
                Rule("chrome-ambiguous", "Alt+Z", HotkeyScope.Global),
            ],
            presence);
        var catalog = HotkeyAttributionCatalog.Create(
            [Probe(f6, HotkeyProbeAvailability.AvailableAtScanTime), Probe(altZ)],
            rules,
            [],
            []);
        var rows = NormalHotkeyInventoryProjector.Project(
            catalog.Items,
            [new HotkeyInventoryOwner(
                "chrome-ambiguous",
                HotkeyInventoryGroup.BackgroundApplication,
                "chrome",
                "Chrome",
                DeepConfirmationEvidenceKind.OfficialRule)]);

        Assert.DoesNotContain(rows, row => row.Item.Gesture == f6);
        var global = Assert.Single(rows);
        Assert.Equal(altZ, global.Item.Gesture);
        Assert.Contains(global.DeepConfirmationCandidates, candidate => candidate.OwnerId == "chrome-ambiguous");
    }

    [Fact]
    public void Background_local_in_app_configuration_is_excluded_while_global_configuration_is_retained()
    {
        var configurations = CurrentStateHotkeyEligibilityPolicy.FilterLocalConfigurations(
            [
                new LocalConfigurationHotkey("chrome", HotkeyGesture.Parse("F6"), "Focus pane", HotkeyScope.Foreground, "local"),
                new LocalConfigurationHotkey("chrome", HotkeyGesture.Parse("Alt+A"), "Overlay", HotkeyScope.Global, "local"),
            ],
            Presence("chrome", ApplicationPresence.Background));

        Assert.Equal("Alt+A", Assert.Single(configurations).Gesture.ToString());
    }

    private static IReadOnlyDictionary<string, ApplicationPresence> Presence(string applicationId, ApplicationPresence presence) =>
        new Dictionary<string, ApplicationPresence>(StringComparer.OrdinalIgnoreCase) { [applicationId] = presence };

    private static RunningRuleHotkey Rule(string applicationId, string gesture, HotkeyScope scope) =>
        new(applicationId, HotkeyGesture.Parse(gesture), "Shortcut", scope, OwnershipConfidence.OfficialDefault, "rule");

    private static HotkeyProbeResult Probe(
        HotkeyGesture gesture,
        HotkeyProbeAvailability availability = HotkeyProbeAvailability.Occupied) => new(
        gesture,
        availability,
        HotkeyProbeMechanism.RegisterHotKeyProbe,
        HotkeyOwner.Unknown,
        DateTimeOffset.UtcNow);
}
