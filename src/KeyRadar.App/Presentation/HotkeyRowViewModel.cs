using System.ComponentModel;
using System.Runtime.CompilerServices;
using KeyRadar.Conflicts;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar;

public sealed class HotkeyRowViewModel : INotifyPropertyChanged
{
    private string _confidenceLabel;

    public HotkeyRowViewModel(
        string gesture,
        string function,
        string scopeLabel,
        string evidenceLabel,
        string confidenceLabel,
        int processId,
        bool canDeepConfirm,
        HotkeyProbeAvailability? probeAvailability = null)
    {
        Gesture = gesture;
        Function = function;
        ScopeLabel = scopeLabel;
        EvidenceLabel = evidenceLabel;
        _confidenceLabel = confidenceLabel;
        ProcessId = processId;
        CanDeepConfirm = canDeepConfirm;
        ProbeAvailability = probeAvailability;
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

    public HotkeyProbeAvailability? ProbeAvailability { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void MarkConfirmed() => ConfidenceLabel = "● 已确认";

    public void MarkPossible() => ConfidenceLabel = "● 可能归属 · 仅规则吻合";

    public void MarkOccupiedUnknown() => ConfidenceLabel = "● 已占用 · 归属未知";

    public static HotkeyRowViewModel Create(
        string gesture,
        string function,
        HotkeyScope scope,
        OwnershipConfidence confidence,
        int processId,
        string? availabilityLabel = null,
        IReadOnlyList<string>? sources = null,
        bool? canDeepConfirm = null) =>
        new(
            gesture,
            function,
            ScopeLabelFor(scope),
            sources is { Count: > 0 } ? "证据：厂商文档 · 官方签名规则包" : "证据：官方签名规则包",
            ConfidenceLabelFor(confidence) + availabilityLabel,
            processId,
            canDeepConfirm ?? (processId > 0 && scope == HotkeyScope.Global));

    public static HotkeyRowViewModel FromProbe(HotkeyProbeResult result) => new(
        result.Gesture.ToString(),
        result.Availability switch
        {
            HotkeyProbeAvailability.Occupied => "功能未知",
            HotkeyProbeAvailability.AvailableAtScanTime => "扫描瞬间可注册",
            HotkeyProbeAvailability.SystemReserved => "系统保留或无法探测",
            _ => $"探测错误{(result.Win32ErrorCode is int code ? $"（{code}）" : string.Empty)}",
        },
        "全局 · RegisterHotKey",
        $"证据：RegisterHotKey 占用探测 · {result.ScannedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
        result.Availability switch
        {
            HotkeyProbeAvailability.Occupied => "● 已占用 · 归属未知",
            HotkeyProbeAvailability.AvailableAtScanTime => "○ 当前可注册",
            HotkeyProbeAvailability.SystemReserved => "◆ 系统保留",
            _ => "! 无法探测",
        },
        processId: 0,
        canDeepConfirm: result.Availability is HotkeyProbeAvailability.Occupied or HotkeyProbeAvailability.ProbeError,
        probeAvailability: result.Availability);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string ScopeLabelFor(HotkeyScope scope) => scope switch
    {
        HotkeyScope.Global => "全局",
        HotkeyScope.WindowsSystem => "Windows 系统",
        _ => "应用内",
    };

    private static string ConfidenceLabelFor(OwnershipConfidence confidence) => confidence switch
    {
        OwnershipConfidence.Confirmed => "● 已确认",
        OwnershipConfidence.LocalConfiguration => "● 配置中发现",
        OwnershipConfidence.SystemKnown => "● 系统已知",
        OwnershipConfidence.Suspected => "● 疑似归属",
        _ => "● 归属未知",
    };
}
