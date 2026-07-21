using System.Diagnostics;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Evidence;

namespace KeyRadar.Windows.DeepConfirmation;

/// <summary>
/// Performs the explicitly-authorized, short active checks for the curated
/// occupied/unknown inventory entries. Work is deliberately serial so one
/// generated gesture cannot overlap another, and all non-detections remain
/// explicit boundary evidence rather than inferred ownership.
/// </summary>
public sealed class HotkeyOwnerTraceCoordinator(
    IHotkeyOwnerTracer tracer,
    TimeSpan? totalBudget = null,
    TimeSpan? perTargetBudget = null,
    int maximumTargets = 12)
{
    private readonly IHotkeyOwnerTracer _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    private readonly TimeSpan _totalBudget = totalBudget ?? TimeSpan.FromSeconds(15);
    private readonly TimeSpan _perTargetBudget = perTargetBudget ?? TimeSpan.FromSeconds(3);
    private readonly int _maximumTargets = maximumTargets;

    public async Task<IReadOnlyDictionary<HotkeyGesture, OwnerTraceResult>> TraceEligibleAsync(
        HotkeyAttributionCatalog catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (_totalBudget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(totalBudget));
        if (_perTargetBudget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(perTargetBudget));
        if (_maximumTargets <= 0) throw new ArgumentOutOfRangeException(nameof(maximumTargets));

        var results = new Dictionary<HotkeyGesture, OwnerTraceResult>();
        var started = Stopwatch.GetTimestamp();
        var targets = HotkeyOwnerTracePolicy.CreateTargets(catalog)
            .Take(_maximumTargets)
            .ToArray();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = _totalBudget - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                results[target.Gesture] = new OwnerTraceResult(OwnerTraceStatus.TimedOut);
                continue;
            }

            using var perTarget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            perTarget.CancelAfter(remaining < _perTargetBudget ? remaining : _perTargetBudget);
            try
            {
                var result = await _tracer.TraceAsync(target, perTarget.Token).ConfigureAwait(false);
                results[target.Gesture] = perTarget.IsCancellationRequested && !cancellationToken.IsCancellationRequested &&
                    result.Status == OwnerTraceStatus.Canceled
                    ? new OwnerTraceResult(OwnerTraceStatus.TimedOut)
                    : result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                results[target.Gesture] = new OwnerTraceResult(OwnerTraceStatus.TimedOut);
            }
        }

        return results;
    }
}
