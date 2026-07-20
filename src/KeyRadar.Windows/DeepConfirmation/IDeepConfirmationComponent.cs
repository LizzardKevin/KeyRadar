using KeyRadar.Hotkeys;

namespace KeyRadar.Windows.DeepConfirmation;

public interface IDeepConfirmationComponent
{
    Task LoadAsync(int processId, CancellationToken cancellationToken);

    Task<DeepConfirmationResult> WaitForTargetAsync(
        HotkeyGesture target,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task UnloadAsync(CancellationToken cancellationToken);
}
