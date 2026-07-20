using KeyRadar.Conflicts;

namespace KeyRadar;

public sealed class ShortcutRowViewModel
{
    public ShortcutRowViewModel(
        string gesture,
        string function,
        string scopeLabel,
        string confidenceLabel,
        int processId,
        bool canJump)
    {
        Gesture = gesture;
        Function = function;
        ScopeLabel = scopeLabel;
        ConfidenceLabel = confidenceLabel;
        ProcessId = processId;
        CanJump = canJump;
    }

    public string Gesture { get; set; }

    public string Function { get; set; }

    public string ScopeLabel { get; set; }

    public string ConfidenceLabel { get; set; }

    public int ProcessId { get; set; }

    public bool CanJump { get; set; }

    public static ShortcutRowViewModel Create(
        string gesture,
        string function,
        ShortcutScope scope,
        OwnershipConfidence confidence,
        int processId,
        string? availabilityLabel = null) =>
        new(
            gesture,
            function,
            ScopeLabelFor(scope),
            ConfidenceLabelFor(confidence) + availabilityLabel,
            processId,
            processId > 0);

    private static string ScopeLabelFor(ShortcutScope scope) => scope switch
    {
        ShortcutScope.Global => "全局",
        ShortcutScope.WindowsSystem => "Windows 系统",
        _ => "应用内",
    };

    private static string ConfidenceLabelFor(OwnershipConfidence confidence) => confidence switch
    {
        OwnershipConfidence.Confirmed => "● 已确认",
        OwnershipConfidence.Configuration => "● 配置中发现",
        OwnershipConfidence.SystemKnown => "● 系统已知",
        OwnershipConfidence.Suspected => "● 疑似归属",
        _ => "● 归属未知",
    };
}
