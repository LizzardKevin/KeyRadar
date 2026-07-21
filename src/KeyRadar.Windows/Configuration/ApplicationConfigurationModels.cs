using KeyRadar.Conflicts;
using KeyRadar.Hotkeys;
using KeyRadar.Rules;
using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Configuration;

public sealed record RunningApplicationVariant(
    ProcessDescriptor Process,
    ApplicationVariantRule Variant);

public sealed record LocalConfigurationHotkey(
    string ApplicationId,
    HotkeyGesture Gesture,
    string Function,
    HotkeyScope Scope,
    string Evidence,
    string? OwnerIdentity = null,
    string? VariantId = null,
    string? CommandId = null);

public interface IApplicationConfigurationReader
{
    bool Supports(ApplicationVariantRule variant);

    Task<IReadOnlyList<LocalConfigurationHotkey>> ReadAsync(
        ProcessDescriptor process,
        ApplicationVariantRule variant,
        CancellationToken cancellationToken);
}
