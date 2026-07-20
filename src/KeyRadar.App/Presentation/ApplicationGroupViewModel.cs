namespace KeyRadar;

public sealed class ApplicationGroupViewModel
{
    public ApplicationGroupViewModel(
        string id,
        string displayName,
        string presenceLabel,
        string evidenceSummary,
        string iconGlyph,
        bool isExpanded,
        IReadOnlyList<HotkeyRowViewModel> hotkeys,
        int processId = 0,
        bool isForeground = false)
    {
        Id = id;
        DisplayName = displayName;
        PresenceLabel = presenceLabel;
        EvidenceSummary = evidenceSummary;
        IconGlyph = iconGlyph;
        IsExpanded = isExpanded;
        Hotkeys = hotkeys;
        ProcessId = processId;
        IsForeground = isForeground;
    }

    public string Id { get; set; }

    public string DisplayName { get; set; }

    public string PresenceLabel { get; set; }

    public string EvidenceSummary { get; set; }

    public string IconGlyph { get; set; }

    public bool IsExpanded { get; set; }

    public IReadOnlyList<HotkeyRowViewModel> Hotkeys { get; set; }

    public int ProcessId { get; set; }

    public bool IsForeground { get; set; }

    public bool CanJump => ProcessId > 0;

    public string HotkeyCountLabel => UiText.Pick($"{Hotkeys.Count} 个热键", $"{Hotkeys.Count} hotkeys");
}
