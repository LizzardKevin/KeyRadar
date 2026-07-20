using KeyRadar.Windows.Applications;
using KeyRadar.Windows.Evidence;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class RunningApplicationSelectionPolicyTests
{
    [Fact]
    public void Equal_ranked_background_processes_use_process_and_variant_tie_breakers()
    {
        var selected = RunningApplicationSelectionPolicy.SelectRepresentative(
        [
            new RunningApplicationSelectionCandidate(202, "sharex", "stable", ApplicationPresence.Background),
            new RunningApplicationSelectionCandidate(101, "sharex", "legacy", ApplicationPresence.Background),
            new RunningApplicationSelectionCandidate(101, "sharex", "stable", ApplicationPresence.Background),
        ]);

        Assert.Equal(101, selected.ProcessId);
        Assert.Equal("legacy", selected.VariantId);
    }
}
