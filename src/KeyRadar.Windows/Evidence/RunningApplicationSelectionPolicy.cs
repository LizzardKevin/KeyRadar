using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Evidence;

public sealed record RunningApplicationSelectionCandidate(
    int ProcessId,
    string ApplicationId,
    string VariantId,
    ApplicationPresence Presence);

public static class RunningApplicationSelectionPolicy
{
    public static RunningApplicationSelectionCandidate SelectRepresentative(
        IEnumerable<RunningApplicationSelectionCandidate> candidates) => candidates
        .OrderByDescending(candidate => candidate.Presence == ApplicationPresence.Foreground)
        .ThenBy(candidate => candidate.ProcessId)
        .ThenBy(candidate => candidate.VariantId, StringComparer.Ordinal)
        .ThenBy(candidate => candidate.ApplicationId, StringComparer.Ordinal)
        .First();
}
