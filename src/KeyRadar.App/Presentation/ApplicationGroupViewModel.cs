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
        IReadOnlyList<ShortcutRowViewModel> shortcuts,
        int processId = 0)
    {
        Id = id;
        DisplayName = displayName;
        PresenceLabel = presenceLabel;
        EvidenceSummary = evidenceSummary;
        IconGlyph = iconGlyph;
        IsExpanded = isExpanded;
        Shortcuts = shortcuts;
        ProcessId = processId;
    }

    public string Id { get; set; }

    public string DisplayName { get; set; }

    public string PresenceLabel { get; set; }

    public string EvidenceSummary { get; set; }

    public string IconGlyph { get; set; }

    public bool IsExpanded { get; set; }

    public IReadOnlyList<ShortcutRowViewModel> Shortcuts { get; set; }

    public int ProcessId { get; set; }

    public bool CanJump => ProcessId > 0;

    public string ShortcutCountLabel => $"{Shortcuts.Count} 个快捷键";
}
