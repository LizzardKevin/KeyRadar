namespace KeyRadar.Windows.Evidence;

/// <summary>Matches a hotkey's display-safe gesture, function, and owner text.</summary>
public static class HotkeyInventorySearch
{
    public static bool Matches(
        string? query,
        string gesture,
        string function,
        string ownerText) =>
        string.IsNullOrWhiteSpace(query) ||
        gesture.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        function.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        ownerText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
