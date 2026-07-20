namespace KeyRadar.Windows.Evidence;

/// <summary>
/// Allows only the latest scan to update UI state.
/// </summary>
public sealed class ScanGenerationCoordinator
{
    private readonly object _gate = new();
    private long _currentGeneration;

    public ScanGeneration Begin()
    {
        lock (_gate)
        {
            return new ScanGeneration(this, checked(++_currentGeneration));
        }
    }

    public bool TryApply(ScanGeneration scan, Action update)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(update);

        lock (_gate)
        {
            if (!ReferenceEquals(scan.Coordinator, this) || scan.Value != _currentGeneration)
            {
                return false;
            }

            update();
            return true;
        }
    }
}

public sealed class ScanGeneration
{
    internal ScanGeneration(ScanGenerationCoordinator coordinator, long value)
    {
        Coordinator = coordinator;
        Value = value;
    }

    internal ScanGenerationCoordinator Coordinator { get; }

    internal long Value { get; }
}
