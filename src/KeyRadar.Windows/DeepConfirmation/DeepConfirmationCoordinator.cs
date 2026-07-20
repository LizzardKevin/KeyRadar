namespace KeyRadar.Windows.DeepConfirmation;

public sealed class DeepConfirmationCoordinator(IDeepConfirmationComponent component)
{
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(30);

    public async Task<DeepConfirmationResult> ConfirmAsync(
        DeepConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The target process must be positive.");
        }

        if (request.Timeout <= TimeSpan.Zero || request.Timeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Deep confirmation must last from 1 to 30 seconds.");
        }

        var loaded = false;
        try
        {
            await component.LoadAsync(request.ProcessId, cancellationToken).ConfigureAwait(false);
            loaded = true;
            return await component
                .WaitForTargetAsync(request.Target, request.Timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return DeepConfirmationResult.Cancelled;
        }
        catch
        {
            return DeepConfirmationResult.Failed;
        }
        finally
        {
            if (loaded)
            {
                await component.UnloadAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
