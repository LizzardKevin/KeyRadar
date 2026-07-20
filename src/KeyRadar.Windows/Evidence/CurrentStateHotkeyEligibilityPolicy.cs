using KeyRadar.Conflicts;
using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Configuration;

namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Limits application-declared shortcuts to those that can act in the current
/// desktop state. A background process cannot receive an in-app shortcut.
/// </summary>
public static class CurrentStateHotkeyEligibilityPolicy
{
    public static bool IsEligible(ApplicationPresence presence, HotkeyScope scope) =>
        scope != HotkeyScope.WindowsSystem &&
        (presence == ApplicationPresence.Foreground ||
         scope is HotkeyScope.Global or HotkeyScope.Background);

    public static bool IsWindowsSystemEligible(HotkeyScope scope) =>
        scope == HotkeyScope.WindowsSystem;

    public static IReadOnlyList<RunningRuleHotkey> FilterRunningRules(
        IEnumerable<RunningRuleHotkey> rules,
        IReadOnlyDictionary<string, ApplicationPresence> presenceByApplicationId) =>
        rules.Where(rule => IsEligible(rule.ApplicationId, rule.Scope, presenceByApplicationId)).ToArray();

    public static IReadOnlyList<LocalConfigurationHotkey> FilterLocalConfigurations(
        IEnumerable<LocalConfigurationHotkey> configurations,
        IReadOnlyDictionary<string, ApplicationPresence> presenceByApplicationId) =>
        configurations.Where(configuration => IsEligible(
            configuration.ApplicationId,
            configuration.Scope,
            presenceByApplicationId)).ToArray();

    public static IReadOnlyList<RunningRuleHotkey> FilterRunningRulesByOwnerIdentity(
        IEnumerable<RunningRuleHotkey> rules,
        IReadOnlyDictionary<string, ApplicationPresence> presenceByOwnerIdentity) =>
        rules.Where(rule => IsEligible(rule.OwnerIdentity ?? rule.ApplicationId, rule.Scope, presenceByOwnerIdentity)).ToArray();

    public static IReadOnlyList<LocalConfigurationHotkey> FilterLocalConfigurationsByOwnerIdentity(
        IEnumerable<LocalConfigurationHotkey> configurations,
        IReadOnlyDictionary<string, ApplicationPresence> presenceByOwnerIdentity) =>
        configurations.Where(configuration => IsEligible(
            configuration.OwnerIdentity ?? configuration.ApplicationId,
            configuration.Scope,
            presenceByOwnerIdentity)).ToArray();

    public static bool IsEligible(
        string applicationId,
        HotkeyScope scope,
        IReadOnlyDictionary<string, ApplicationPresence> presenceByApplicationId) =>
        presenceByApplicationId.TryGetValue(applicationId, out var presence) &&
        IsEligible(presence, scope);
}
