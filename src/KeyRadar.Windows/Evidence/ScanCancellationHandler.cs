namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Converts an expected scan cancellation into an incomplete scan result.
/// </summary>
public static class ScanCancellationHandler
{
    public static async Task<bool> TryRunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
