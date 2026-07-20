using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.SystemState;

namespace KeyRadar.Windows.Tests.DeepConfirmation;

public sealed class WindowsSystemDeepConfirmationCandidateBuilderTests
{
    [Fact]
    public void Current_system_rules_are_combined_with_eligible_application_candidates()
    {
        var target = HotkeyGesture.Parse("Alt+F4");

        var candidates = WindowsSystemDeepConfirmationCandidateBuilder.Build(
            target,
            [new RunningRuleHotkey(
                WindowsSystemHotkeyIdentity.ApplicationId,
                target,
                "Close active window",
                HotkeyScope.WindowsSystem,
                OwnershipConfidence.SystemKnown,
                "Windows documentation")],
            new WindowsSessionState("Windows", "English", false),
            [new DeepConfirmationCandidate("sharex", "ShareX", DeepConfirmationEvidenceKind.OfficialRule, "101\u001Fsharex\u001Fstable")]);

        Assert.Equal(["sharex", WindowsSystemHotkeyIdentity.ApplicationId], candidates.Select(candidate => candidate.OwnerId));
    }

    [Fact]
    public void Current_print_screen_setting_is_local_windows_evidence()
    {
        var candidates = WindowsSystemDeepConfirmationCandidateBuilder.Build(
            HotkeyGesture.Parse("PrintScreen"),
            [],
            new WindowsSessionState("Windows", "English", true),
            []);

        var candidate = Assert.Single(candidates);
        Assert.Equal(WindowsSystemHotkeyIdentity.ApplicationId, candidate.OwnerId);
        Assert.Equal(DeepConfirmationEvidenceKind.LocalConfiguration, candidate.Evidence);
    }
}
