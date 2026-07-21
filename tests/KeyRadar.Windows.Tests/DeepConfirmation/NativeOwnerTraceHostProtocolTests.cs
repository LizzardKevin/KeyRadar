using KeyRadar.Windows.DeepConfirmation;

namespace KeyRadar.Windows.Tests.DeepConfirmation;

public sealed class NativeOwnerTraceHostProtocolTests
{
    [Fact]
    public void TryParseResult_preserves_a_detected_owner_thread()
    {
        var parsed = NativeOwnerTraceHostProtocol.TryParseResult(Result("1,42,7,0"), Identity, out var result);

        Assert.True(parsed);
        Assert.Equal(OwnerTraceStatus.Detected, result.Status);
        Assert.Equal(42, result.ProcessId);
        Assert.Equal(7, result.ThreadId);
    }

    [Fact]
    public void TryParseResult_rejects_malformed_or_non_owner_result()
    {
        Assert.False(NativeOwnerTraceHostProtocol.TryParseResult(Result("1,not-a-pid,7,0"), Identity, out _));
        Assert.False(NativeOwnerTraceHostProtocol.TryParseResult(Result("1,42,7,0", ownerPid: 901), Identity, out _));
        Assert.False(NativeOwnerTraceHostProtocol.TryParseResult(Result("1,42,7,0", ownerStartFileTime: 12639457), Identity, out _));
        Assert.False(NativeOwnerTraceHostProtocol.TryParseResult(Result("1,42,7,0", nonce: "ffffffffffffffffffffffffffffffff"), Identity, out _));
        Assert.True(NativeOwnerTraceHostProtocol.TryParseResult(Result("0,0,0,0"), Identity, out var result));
        Assert.Equal(OwnerTraceStatus.NotDetected, result.Status);
        Assert.Null(result.ProcessId);
        Assert.Null(result.ThreadId);
    }

    private static readonly OwnerTraceHostIdentity Identity = new(
        "0123456789abcdef0123456789abcdef", 75, 6, 900, 12639456);

    private static string Result(string prefix, int ownerPid = 900, long ownerStartFileTime = 12639456, string? nonce = null) =>
        $"{prefix},{OwnerTraceHostIdentity.Magic},{OwnerTraceHostIdentity.Version},{ownerPid},{ownerStartFileTime},75,6,{nonce ?? Identity.Nonce}";
}
