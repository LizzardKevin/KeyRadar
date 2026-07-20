using System.Diagnostics;

namespace KeyRadar.Updater.Updates;

public static class UpdateHealthMonitor
{
    public static async Task<bool> WaitForMarkerAsync(
        string markerPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var fullPath = Path.GetFullPath(markerPath);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath))
            {
                return true;
            }

            var remaining = timeout - stopwatch.Elapsed;
            await Task.Delay(
                remaining < TimeSpan.FromMilliseconds(10) ? remaining : TimeSpan.FromMilliseconds(10),
                cancellationToken).ConfigureAwait(false);
        }

        return File.Exists(fullPath);
    }
}
