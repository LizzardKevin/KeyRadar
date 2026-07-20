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
        HotkeyProbeAvailability? probeAvailability = null,
        bool isGlobal = false,
        bool isUnconfirmed = false)
    {
        Gesture = gesture;
        Function = function;
        ScopeLabel = scopeLabel;
        EvidenceLabel = evidenceLabel;
        _confidenceLabel = confidenceLabel;
        ProcessId = processId;
        CanDeepConfirm = canDeepConfirm;
        ProbeAvailability = probeAvailability;
        IsGlobal = isGlobal;
        IsUnconfirmed = isUnconfirmed;
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

    public bool IsGlobal { get; }

    public bool IsUnconfirmed { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void MarkConfirmed() => ConfidenceLabel = UiText.Pick("● 已确认", "● Confirmed");

    public void MarkPossible() => ConfidenceLabel = UiText.Pick("● 可能归属 · 仅规则吻合", "● Possible owner · rule match only");

    public void MarkOccupiedUnknown() => ConfidenceLabel = UiText.Pick("● 已占用 · 归属未知", "● Occupied · owner unknown");

    public static HotkeyRowViewModel Create(
        string gesture,
        string function,
        HotkeyScope scope,
        OwnershipConfidence confidence,
        int processId,
        string? availabilityLabel = null,
        IReadOnlyList<string>? sources = null,
        bool? canDeepConfirm = null,
        string? evidenceLabel = null) =>
        new(
            gesture,
            function,
            ScopeLabelFor(scope),
            evidenceLabel ?? (sources is { Count: > 0 }
                ? UiText.Pick("证据：厂商文档 · 官方签名规则包", "Evidence: vendor documentation · signed official rule pack")
                : UiText.Pick("证据：官方签名规则包", "Evidence: signed official rule pack")),
            ConfidenceLabelFor(confidence) + availabilityLabel,
            processId,
            canDeepConfirm ?? (processId > 0 && scope == HotkeyScope.Global),
            isGlobal: scope == HotkeyScope.Global,
            isUnconfirmed: confidence is OwnershipConfidence.Suspected or OwnershipConfidence.Unknown or OwnershipConfidence.UserDeclared);

    public static HotkeyRowViewModel FromProbe(HotkeyProbeResult result) => new(
        result.Gesture.ToString(),
        result.Availability switch
        {
            HotkeyProbeAvailability.Occupied => UiText.Pick("功能未知", "Function unknown"),
            HotkeyProbeAvailability.AvailableAtScanTime => UiText.Pick("扫描瞬间可注册", "Available at scan time"),
            HotkeyProbeAvailability.SystemReserved => UiText.Pick("系统保留或无法探测", "System reserved or not probeable"),
            _ => UiText.Pick(
                $"探测错误{(result.Win32ErrorCode is int code ? $"（{code}）" : string.Empty)}",
                $"Probe error{(result.Win32ErrorCode is int errorCode ? $" ({errorCode})" : string.Empty)}"),
        },
        UiText.Pick("全局 · RegisterHotKey", "Global · RegisterHotKey"),
        UiText.Pick("证据：", "Evidence: ") + $"RegisterHotKey · {result.ScannedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
        result.Availability switch
        {
            HotkeyProbeAvailability.Occupied => UiText.Pick("● 已占用 · 归属未知", "● Occupied · owner unknown"),
            HotkeyProbeAvailability.AvailableAtScanTime => UiText.Pick("○ 当前可注册", "○ Available now"),
            HotkeyProbeAvailability.SystemReserved => UiText.Pick("◆ 系统保留", "◆ System reserved"),
            _ => UiText.Pick("! 无法探测", "! Not probeable"),
        },
        processId: 0,
        canDeepConfirm: result.Availability is HotkeyProbeAvailability.Occupied or HotkeyProbeAvailability.ProbeError,
        probeAvailability: result.Availability,
        isGlobal: true,
        isUnconfirmed: result.Availability is HotkeyProbeAvailability.Occupied or HotkeyProbeAvailability.ProbeError);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string ScopeLabelFor(HotkeyScope scope) => scope switch
    {
        HotkeyScope.Global => UiText.Pick("全局", "Global"),
        HotkeyScope.WindowsSystem => UiText.Pick("Windows 系统", "Windows system"),
        _ => UiText.Pick("应用内", "In-app"),
    };

    private static string ConfidenceLabelFor(OwnershipConfidence confidence) => confidence switch
    {
        OwnershipConfidence.Confirmed => UiText.Pick("● 已确认", "● Confirmed"),
        OwnershipConfidence.LocalConfiguration => UiText.Pick("● 配置中发现", "● Found in local config"),
        OwnershipConfidence.HardwareMapping => UiText.Pick("● 硬件映射已发现", "● Found in hardware mapping"),
        OwnershipConfidence.SystemKnown => UiText.Pick("● 系统已知", "● Windows known"),
        OwnershipConfidence.Corroborated => UiText.Pick("● 交叉佐证", "● Corroborated"),
        OwnershipConfidence.OfficialDefault => UiText.Pick("● 官方默认", "● Official default"),
        OwnershipConfidence.UserDeclared => UiText.Pick("● 用户声明 · 未经官方验证", "● User declared · not officially verified"),
        OwnershipConfidence.Suspected => UiText.Pick("● 疑似归属", "● Suspected owner"),
        _ => UiText.Pick("● 归属未知", "● Owner unknown"),
    };
}
