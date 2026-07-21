using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Configuration;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class CurrentEffectiveHotkeyPresentationTests
{
    [Fact]
    public void Nvidia_group_projects_the_configured_gesture_once_and_keeps_another_owners_alt_r()
    {
        var nvidiaOwner = "18:nvidia-app:overlay";
        var rules = new[]
        {
            Rule("nvidia-app", "Alt+Z", "Open overlay", nvidiaOwner, "open-overlay"),
            Rule("nvidia-app", "Alt+R", "Performance overlay", nvidiaOwner, "performance-overlay-toggle"),
            Rule("other-app", "Alt+R", "Other command", "27:other-app:default", "other-command"),
        };
        var local = new[]
        {
            new LocalConfigurationHotkey(
                "nvidia-app",
                HotkeyGesture.Parse("Alt+I"),
                "Performance overlay",
                HotkeyScope.Global,
                "NVIDIA local configuration",
                nvidiaOwner,
                "overlay",
                "performance-overlay-toggle"),
        };

        var effective = CurrentEffectiveHotkeyProjection.Project(rules, local);
        Assert.Contains(effective.DiagnosticRules, rule =>
            rule.ApplicationId == "nvidia-app" && rule.Gesture == HotkeyGesture.Parse("Alt+R"));
        var catalog = HotkeyAttributionCatalog.Create(
            [],
            effective.Rules,
            effective.LocalConfigurations,
            []);
        var rows = NormalHotkeyInventoryProjector.Project(catalog.Items,
        [
            Owner(nvidiaOwner, "nvidia-app", "NVIDIA"),
            Owner("27:other-app:default", "other-app", "Other"),
        ]);

        var nvidiaRows = rows.Where(row => row.PrimaryGroupId == "nvidia-app").ToArray();
        Assert.Equal(["Alt+I", "Alt+Z"], nvidiaRows.Select(row => row.Item.Gesture.ToString()).OrderBy(gesture => gesture, StringComparer.Ordinal));
        Assert.DoesNotContain(nvidiaRows, row => row.Item.Gesture == HotkeyGesture.Parse("Alt+R"));
        Assert.Single(nvidiaRows, row => HotkeyInventorySearch.Matches("Alt+I", row.Item.Gesture.ToString(), row.Item.Function, "NVIDIA"));
        Assert.DoesNotContain(nvidiaRows, row => HotkeyInventorySearch.Matches("Alt+R", row.Item.Gesture.ToString(), row.Item.Function, "NVIDIA"));

        var otherAltR = Assert.Single(rows, row => row.PrimaryGroupId == "other-app" && row.Item.Gesture == HotkeyGesture.Parse("Alt+R"));
        Assert.Equal("Other command", otherAltR.Item.Function);
    }

    [Fact]
    public void Nvidia_group_retains_the_official_default_when_configuration_is_unavailable()
    {
        var nvidiaOwner = "18:nvidia-app:overlay";
        var effective = CurrentEffectiveHotkeyProjection.Project(
        [
            Rule("nvidia-app", "Alt+R", "Performance overlay", nvidiaOwner, "performance-overlay-toggle"),
        ],
        []);
        var catalog = HotkeyAttributionCatalog.Create([], effective.Rules, effective.LocalConfigurations, []);
        var rows = NormalHotkeyInventoryProjector.Project(catalog.Items,
        [
            Owner(nvidiaOwner, "nvidia-app", "NVIDIA"),
        ]);

        var nvidia = Assert.Single(rows, row => row.PrimaryGroupId == "nvidia-app");
        Assert.Equal(HotkeyGesture.Parse("Alt+R"), nvidia.Item.Gesture);
    }

    private static RunningRuleHotkey Rule(
        string applicationId,
        string gesture,
        string function,
        string ownerIdentity,
        string commandId) => new(
            applicationId,
            HotkeyGesture.Parse(gesture),
            function,
            HotkeyScope.Global,
            OwnershipConfidence.OfficialDefault,
            "Official rule",
            ownerIdentity,
            "overlay",
            commandId);

    private static HotkeyInventoryOwner Owner(string identity, string groupId, string displayName) => new(
        identity,
        HotkeyInventoryGroup.BackgroundApplication,
        groupId,
        displayName,
        DeepConfirmationEvidenceKind.OfficialRule,
        DisplayOwnerId: groupId);
}
