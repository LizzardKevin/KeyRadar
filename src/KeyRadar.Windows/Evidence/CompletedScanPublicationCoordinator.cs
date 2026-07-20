namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Creates and publishes a completed scan only while its operation remains current.
/// </summary>
public sealed class CompletedScanPublicationCoordinator(CompletedScanStateStore stateStore)
{
    private readonly CompletedScanStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));

    public void CreateAndPublish(
        Func<CompletedScanExportSnapshot> createSnapshot,
        CancellationToken cancellationToken)
        => CreateAndPublish(createSnapshot, cancellationToken, static () => { });

    /// <summary>
    /// Publishes a completed scan and then commits its corresponding UI state.
    /// </summary>
    public void CreateAndPublish(
        Func<CompletedScanExportSnapshot> createSnapshot,
        CancellationToken cancellationToken,
        Action afterPublication)
    {
        ArgumentNullException.ThrowIfNull(createSnapshot);
        ArgumentNullException.ThrowIfNull(afterPublication);

        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = createSnapshot();
        cancellationToken.ThrowIfCancellationRequested();
        _stateStore.Publish(snapshot);
        afterPublication();
    }
}
