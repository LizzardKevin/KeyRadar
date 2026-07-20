using KeyRadar.Hotkeys;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.DeepConfirmation;

public enum DeepConfirmationEvidenceKind
{
    OfficialRule,
    LocalConfiguration,
    ActiveHardwareProfile,
    UserDeclaredHardwareProfile,
}

public enum DeepConfirmationConclusion
{
    ConfirmedOwner,
    PossibleOwner,
    OccupiedOwnerUnknown,
    UnableToConfirm,
}

public sealed record DeepConfirmationCandidate(
    string OwnerId,
    string DisplayName,
    DeepConfirmationEvidenceKind Evidence);

public sealed record ImmediateDeepConfirmationRequest(
    HotkeyGesture Target,
    IReadOnlyList<DeepConfirmationCandidate> Candidates);

public sealed record ImmediateDeepConfirmationResult(
    DeepConfirmationConclusion Conclusion,
    HotkeyProbeResult Probe,
    IReadOnlyList<DeepConfirmationCandidate> Candidates,
    IReadOnlyList<NativeHotkeyProbeResult> NativeProbes);

public sealed class ImmediateDeepConfirmationService(
    Func<HotkeyGesture, HotkeyProbeResult> reprobe,
    Func<HotkeyGesture, CancellationToken, Task<IReadOnlyList<DeepConfirmationCandidate>>>? refreshEvidence = null,
    Func<HotkeyGesture, CancellationToken, Task<IReadOnlyList<NativeHotkeyProbeResult>>>? nativeReprobe = null)
{
    public async Task<ImmediateDeepConfirmationResult> ConfirmAsync(
        ImmediateDeepConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var refreshed = refreshEvidence is null
            ? []
            : await refreshEvidence(request.Target, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = request.Candidates
            .Concat(refreshed)
            .DistinctBy(candidate => $"{candidate.OwnerId}\u001F{candidate.Evidence}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var probe = reprobe(request.Target);
        var nativeProbes = nativeReprobe is null
            ? []
            : await nativeReprobe(request.Target, cancellationToken).ConfigureAwait(false);
        var nativeDisagrees = nativeProbes
            .Where(result => result.Availability != HotkeyProbeAvailability.ProbeError)
            .Any(result => result.Availability != probe.Availability);
        var exact = candidates
            .Where(candidate => candidate.Evidence is
                DeepConfirmationEvidenceKind.LocalConfiguration or
                DeepConfirmationEvidenceKind.ActiveHardwareProfile)
            .ToArray();
        var conclusion = exact.Length > 0
            ? DeepConfirmationConclusion.ConfirmedOwner
            : nativeDisagrees
                ? DeepConfirmationConclusion.UnableToConfirm
            : probe.Availability == HotkeyProbeAvailability.Occupied && candidates.Length > 0
                ? DeepConfirmationConclusion.PossibleOwner
                : probe.Availability == HotkeyProbeAvailability.Occupied
                    ? DeepConfirmationConclusion.OccupiedOwnerUnknown
                    : DeepConfirmationConclusion.UnableToConfirm;

        return new ImmediateDeepConfirmationResult(
            conclusion,
            probe,
            exact.Length > 0 ? exact : candidates,
            nativeProbes);
    }
}
