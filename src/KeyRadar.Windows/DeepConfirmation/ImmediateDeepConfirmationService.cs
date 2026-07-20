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
    DeepConfirmationEvidenceKind Evidence,
    string? EvidenceIdentity = null);

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

        var refreshSucceeded = false;
        var refreshFailed = false;
        IReadOnlyList<DeepConfirmationCandidate> refreshed = [];
        if (refreshEvidence is not null)
        {
            try
            {
                refreshed = await refreshEvidence(request.Target, cancellationToken).ConfigureAwait(false);
                refreshSucceeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Stale request evidence can describe context, but it cannot
                // establish current ownership when the refresh is unavailable.
                refreshFailed = true;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = DeepConfirmationCandidateOrdering.Canonicalize(
            refreshSucceeded ? refreshed : request.Candidates);
        var probe = reprobe(request.Target);
        var nativeProbes = nativeReprobe is null
            ? []
            : await nativeReprobe(request.Target, cancellationToken).ConfigureAwait(false);
        var nativeDisagrees = nativeProbes
            .Where(result => result.Availability != HotkeyProbeAvailability.ProbeError)
            .Any(result => result.Availability != probe.Availability);
        var exact = refreshSucceeded
            ? candidates
            .Where(candidate => candidate.Evidence is
                DeepConfirmationEvidenceKind.LocalConfiguration or
                DeepConfirmationEvidenceKind.ActiveHardwareProfile)
            .ToArray()
            : [];
        var canConfirmExact = refreshSucceeded &&
            exact.Length > 0 &&
            probe.Availability == HotkeyProbeAvailability.Occupied &&
            !nativeDisagrees;
        var conclusion = refreshFailed
            ? DeepConfirmationConclusion.UnableToConfirm
            : canConfirmExact
            ? DeepConfirmationConclusion.ConfirmedOwner
            : nativeDisagrees
                ? DeepConfirmationConclusion.UnableToConfirm
            : probe.Availability == HotkeyProbeAvailability.Occupied && candidates.Count > 0
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
