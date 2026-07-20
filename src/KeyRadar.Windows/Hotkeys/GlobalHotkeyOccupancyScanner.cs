using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.Hotkeys;

public enum HotkeyProbeSafety
{
    Safe,
    Pause,
    Cancel,
}

public interface IHotkeyProbeSafetyGate
{
    HotkeyProbeSafety Check();
}

public sealed record HotkeyScanProgress(int Completed, int Total, HotkeyGesture Current);

public sealed class GlobalHotkeyOccupancyScanner(
    GlobalHotkeyAvailabilityProbe probe,
    IHotkeyProbeSafetyGate safetyGate,
    int batchSize = 16)
{
    public async Task<IReadOnlyList<HotkeyProbeResult>> ScanAsync(
        IReadOnlyList<HotkeyGesture> candidates,
        IProgress<HotkeyScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        var results = new List<HotkeyProbeResult>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safety = safetyGate.Check();
            if (safety == HotkeyProbeSafety.Cancel)
            {
                break;
            }

            while (safety == HotkeyProbeSafety.Pause)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                safety = safetyGate.Check();
                if (safety == HotkeyProbeSafety.Cancel)
                {
                    return results;
                }
            }

            var gesture = candidates[index];
            results.Add(probe.Probe(gesture));
            progress?.Report(new HotkeyScanProgress(index + 1, candidates.Count, gesture));

            if ((index + 1) % batchSize == 0)
            {
                await Task.Yield();
            }
        }

        return results;
    }
}
