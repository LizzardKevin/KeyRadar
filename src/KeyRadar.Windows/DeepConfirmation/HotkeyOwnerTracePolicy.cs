using KeyRadar.Hotkeys;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.DeepConfirmation;

/// <summary>
/// Keeps experimental active owner tracing opt-in and restricted to the same
/// curated occupied/unknown entries that are already shown to the user.
/// </summary>
public static class HotkeyOwnerTracePolicy
{
    public static IReadOnlyList<HotkeyOwnerTraceTarget> CreateTargets(HotkeyAttributionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.ActionableUnknownProbes
            .Where(probe => probe.Availability == HotkeyProbeAvailability.Occupied)
            .Where(probe => IsSafeToSendInput(probe.Gesture))
            .Select(probe => new HotkeyOwnerTraceTarget(probe.Gesture))
            .DistinctBy(target => target.Gesture)
            .OrderBy(target => target.Gesture.ToString(), StringComparer.Ordinal)
            .ToArray();
    }

    public static bool IsEligible(HotkeyAttributionCatalog catalog, HotkeyGesture gesture) =>
        CreateTargets(catalog).Any(target => target.Gesture == gesture);

    private static bool IsSafeToSendInput(HotkeyGesture gesture)
    {
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Windows) ||
            gesture.Key.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt) &&
            gesture.Key is "F4" or "Tab" or "Esc" or "Space")
        {
            return false;
        }

        // Ctrl+Shift+Esc opens Task Manager; keep this named separately so a later
        // narrowing of the Ctrl+Esc rule cannot accidentally make it injectable.
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control) &&
            gesture.Modifiers.HasFlag(HotkeyModifiers.Shift) &&
            gesture.Key.Equals("Esc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control) &&
            gesture.Key.Equals("Esc", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !(gesture.Modifiers.HasFlag(HotkeyModifiers.Control) &&
                 gesture.Modifiers.HasFlag(HotkeyModifiers.Alt) &&
                 gesture.Key.Equals("Delete", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record HotkeyOwnerTraceTarget(HotkeyGesture Gesture);
