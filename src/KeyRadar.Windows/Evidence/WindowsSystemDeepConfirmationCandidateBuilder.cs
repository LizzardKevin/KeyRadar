using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.SystemState;

namespace KeyRadar.Windows.Evidence;

public static class WindowsSystemHotkeyIdentity
{
    public const string ApplicationId = "windows-system";
    public const string GroupId = ApplicationId;

    public static bool IsSystemApplication(string applicationId) =>
        applicationId.Equals(ApplicationId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Adds current Windows-owned evidence to an otherwise application-eligible
/// deep-confirmation candidate set. Windows evidence never enters the
/// application presence policy.
/// </summary>
public static class WindowsSystemDeepConfirmationCandidateBuilder
{
    public static IReadOnlyList<DeepConfirmationCandidate> Build(
        HotkeyGesture target,
        IEnumerable<RunningRuleHotkey> currentSystemRules,
        WindowsSessionState session,
        IEnumerable<DeepConfirmationCandidate> eligibleApplicationCandidates)
    {
        ArgumentNullException.ThrowIfNull(currentSystemRules);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(eligibleApplicationCandidates);

        var systemRules = currentSystemRules
            .Where(rule => WindowsSystemHotkeyIdentity.IsSystemApplication(rule.ApplicationId))
            .Where(rule => rule.Gesture == target && rule.Scope == HotkeyScope.WindowsSystem)
            .Select(_ => new DeepConfirmationCandidate(
                WindowsSystemHotkeyIdentity.ApplicationId,
                WindowsSystemHotkeyIdentity.ApplicationId,
                DeepConfirmationEvidenceKind.OfficialRule));
        IReadOnlyList<DeepConfirmationCandidate> printScreenConfiguration = session.PrintScreenOpensSnippingTool == true &&
            target == HotkeyGesture.Parse("PrintScreen")
            ? [new DeepConfirmationCandidate(
                WindowsSystemHotkeyIdentity.ApplicationId,
                WindowsSystemHotkeyIdentity.ApplicationId,
                DeepConfirmationEvidenceKind.LocalConfiguration)]
            : [];

        return DeepConfirmationCandidateOrdering.Canonicalize(
            eligibleApplicationCandidates.Concat(systemRules).Concat(printScreenConfiguration));
    }
}
