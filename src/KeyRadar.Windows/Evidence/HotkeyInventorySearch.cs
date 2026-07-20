namespace KeyRadar.Windows.Evidence;

/// <summary>Matches only presentation-safe text that is already approved for a row.</summary>
public static class HotkeyInventorySearch
{
    public static bool Matches(
        string? query,
        string gesture,
        string function,
        string ownerAndEvidenceText) =>
        string.IsNullOrWhiteSpace(query) ||
        gesture.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        function.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        ownerAndEvidenceText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
