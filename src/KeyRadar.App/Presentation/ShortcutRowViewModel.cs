using System.ComponentModel;
using System.Runtime.CompilerServices;
using KeyRadar.Conflicts;
using KeyRadar.Rules;

namespace KeyRadar;

public sealed class ShortcutRowViewModel : INotifyPropertyChanged
{
    private string _confidenceLabel;

    public ShortcutRowViewModel(
        string gesture,
        string function,
        string scopeLabel,
        string evidenceLabel,
        string confidenceLabel,
        int processId,
        bool canDeepConfirm)
    {
        Gesture = gesture;
        Function = function;
        ScopeLabel = scopeLabel;
        EvidenceLabel = evidenceLabel;
        _confidenceLabel = confidenceLabel;
        ProcessId = processId;
        CanDeepConfirm = canDeepConfirm;
    }

    public string Gesture { get; set; }

    public string Function { get; set; }

    public string ScopeLabel { get; set; }

    public string EvidenceLabel { get; set; }

    public string ConfidenceLabel
    {
        get => _confidenceLabel;
        set
        {
            if (_confidenceLabel == value) return;
            _confidenceLabel = value;
            OnPropertyChanged();
        }
    }

    public int ProcessId { get; set; }

    public bool CanDeepConfirm { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void MarkConfirmed() => ConfidenceLabel = "● 已确认";

    public static ShortcutRowViewModel Create(
        string gesture,
        string function,
        ShortcutScope scope,
        OwnershipConfidence confidence,
        int processId,
        string? availabilityLabel = null,
        IReadOnlyList<string>? sources = null,
        RuleOrigin origin = RuleOrigin.BuiltIn) =>
        new(
            gesture,
            function,
            ScopeLabelFor(scope),
            EvidenceLabelFor(origin, sources),
            ConfidenceLabelFor(confidence) + availabilityLabel,
            processId,
            processId > 0 && scope == ShortcutScope.Global);

    private static string EvidenceLabelFor(RuleOrigin origin, IReadOnlyList<string>? sources) => origin switch
    {
        RuleOrigin.LocalUnsigned => "证据：未签名本地规则",
        RuleOrigin.SignedRulePack when sources is { Count: > 0 } => "证据：厂商文档 · 官方签名规则包",
        RuleOrigin.SignedRulePack => "证据：官方签名规则包",
        _ when sources is { Count: > 0 } => "证据：厂商官方文档",
        _ => "证据：KeyRadar 内置规则",
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
