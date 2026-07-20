using KeyRadar.Hotkeys;
using KeyRadar.Windows.DeepConfirmation;

namespace KeyRadar.Windows.Tests.DeepConfirmation;

public sealed class DeepConfirmationCoordinatorTests
{
    [Fact]
    public async Task Confirmed_session_unloads_the_component()
    {
        var component = new StubComponent(DeepConfirmationResult.Confirmed);
        var coordinator = new DeepConfirmationCoordinator(component);

        var result = await coordinator.ConfirmAsync(
            new DeepConfirmationRequest(42, HotkeyGesture.Parse("Alt+A"), TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        Assert.Equal(DeepConfirmationResult.Confirmed, result);
        Assert.Equal(1, component.LoadCalls);
        Assert.Equal(1, component.UnloadCalls);
    }

    [Fact]
    public async Task Cancellation_still_unloads_the_component()
    {
        var component = new StubComponent(DeepConfirmationResult.Cancelled, throwCancellation: true);
        var coordinator = new DeepConfirmationCoordinator(component);

        var result = await coordinator.ConfirmAsync(
            new DeepConfirmationRequest(42, HotkeyGesture.Parse("Alt+A"), TimeSpan.FromSeconds(30)),
            new CancellationToken(canceled: true));

        Assert.Equal(DeepConfirmationResult.Cancelled, result);
        Assert.Equal(1, component.UnloadCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task Timeout_must_be_between_one_and_thirty_seconds(int seconds)
    {
        var coordinator = new DeepConfirmationCoordinator(new StubComponent(DeepConfirmationResult.TimedOut));
        var request = new DeepConfirmationRequest(42, HotkeyGesture.Parse("Alt+A"), TimeSpan.FromSeconds(seconds));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            coordinator.ConfirmAsync(request, CancellationToken.None));
    }

    private sealed class StubComponent(
        DeepConfirmationResult result,
        bool throwCancellation = false) : IDeepConfirmationComponent
    {
        public int LoadCalls { get; private set; }

        public int UnloadCalls { get; private set; }

        public Task LoadAsync(int processId, CancellationToken cancellationToken)
        {
            LoadCalls++;
            return Task.CompletedTask;
        }

        public Task<DeepConfirmationResult> WaitForTargetAsync(
            HotkeyGesture target,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (throwCancellation)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return Task.FromResult(result);
        }

        public Task UnloadAsync(CancellationToken cancellationToken)
        {
            UnloadCalls++;
            return Task.CompletedTask;
        }
    }
}
