using System.Globalization;

namespace KeyRadar.Windows.DeepConfirmation;

public enum OwnerTraceStatus
{
    Detected,
    NotDetected,
    NativeSupportUnavailable,
    Canceled,
    TimedOut,
    Failed,
}

public sealed record OwnerTraceResult(
    OwnerTraceStatus Status,
    int? ProcessId = null,
    int? ThreadId = null,
    string? ProcessName = null,
    int? Win32ErrorCode = null);

/// <summary>The values which bind a native result to one controller-created trace.</summary>
internal sealed record OwnerTraceHostIdentity(
    string Nonce,
    uint VirtualKey,
    uint Modifiers,
    int OwnerHostProcessId,
    long OwnerHostStartFileTimeUtc)
{
    public const uint Magic = 0x4B525452; // KRTR
    public const uint Version = 2;
}

/// <summary>
/// Parser for a native result. Results are accepted only when their magic, protocol
/// version, owner helper PID, gesture, and per-trace nonce all match. The named
/// mapping is IPC, not an authentication boundary against a hostile same-user process.
/// </summary>
public static class NativeOwnerTraceHostProtocol
{
    // status,pid,tid,win32Error,magic,version,ownerPid,ownerStartFileTime,vk,mod,nonce
    internal static bool TryParseResult(string? text, OwnerTraceHostIdentity expected, out OwnerTraceResult result)
    {
        ArgumentNullException.ThrowIfNull(expected);
        result = new OwnerTraceResult(OwnerTraceStatus.Failed);
        var values = text?.Trim().Split(',');
        if (values is not { Length: 11 } ||
            !uint.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var status) ||
            !int.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out var processId) ||
            !int.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture, out var threadId) ||
            !int.TryParse(values[3], NumberStyles.None, CultureInfo.InvariantCulture, out var errorCode) ||
            !uint.TryParse(values[4], NumberStyles.None, CultureInfo.InvariantCulture, out var magic) ||
            !uint.TryParse(values[5], NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            !int.TryParse(values[6], NumberStyles.None, CultureInfo.InvariantCulture, out var ownerProcessId) ||
            !long.TryParse(values[7], NumberStyles.None, CultureInfo.InvariantCulture, out var ownerStartFileTime) ||
            !uint.TryParse(values[8], NumberStyles.None, CultureInfo.InvariantCulture, out var virtualKey) ||
            !uint.TryParse(values[9], NumberStyles.None, CultureInfo.InvariantCulture, out var modifiers) ||
            status is > 2 || processId < 0 || threadId < 0 || errorCode < 0 || ownerStartFileTime <= 0 ||
            magic != OwnerTraceHostIdentity.Magic || version != OwnerTraceHostIdentity.Version ||
            ownerProcessId != expected.OwnerHostProcessId ||
            ownerStartFileTime != expected.OwnerHostStartFileTimeUtc ||
            virtualKey != expected.VirtualKey || modifiers != expected.Modifiers ||
            !string.Equals(values[10], expected.Nonce, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        result = status == 1 && processId > 0 && threadId > 0
            ? new OwnerTraceResult(OwnerTraceStatus.Detected, processId, threadId,
                Win32ErrorCode: errorCode == 0 ? null : errorCode)
            : new OwnerTraceResult(status == 0 ? OwnerTraceStatus.NotDetected : OwnerTraceStatus.Failed,
                Win32ErrorCode: errorCode == 0 ? null : errorCode);
        return true;
    }
}
