using KeyRadar.Hotkeys;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.DeepConfirmation;

public enum DeepConfirmationEvidenceKind
{
    OfficialRule,
    LocalConfiguration,
    ActiveHardwareProfile,
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
    IReadOnlyList<DeepConfirmationCandidate> Candidates);

public sealed class ImmediateDeepConfirmationService(
    Func<HotkeyGesture, HotkeyProbeResult> reprobe)
{
    public Task<ImmediateDeepConfirmationResult> ConfirmAsync(
        ImmediateDeepConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var probe = reprobe(request.Target);
        var exact = request.Candidates
            .Where(candidate => candidate.Evidence is
                DeepConfirmationEvidenceKind.LocalConfiguration or
                DeepConfirmationEvidenceKind.ActiveHardwareProfile)
            .ToArray();
        var conclusion = exact.Length > 0
            ? DeepConfirmationConclusion.ConfirmedOwner
            : probe.Availability == HotkeyProbeAvailability.Occupied && request.Candidates.Count > 0
                ? DeepConfirmationConclusion.PossibleOwner
                : probe.Availability == HotkeyProbeAvailability.Occupied
                    ? DeepConfirmationConclusion.OccupiedOwnerUnknown
                    : DeepConfirmationConclusion.UnableToConfirm;

        return Task.FromResult(new ImmediateDeepConfirmationResult(
            conclusion,
            probe,
            exact.Length > 0 ? exact : request.Candidates));
    }
}
