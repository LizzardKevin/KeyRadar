using KeyRadar.Diagnostics;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Evidence;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.Evidence;

public sealed class CompletedScanStateStoreTests
{
    [Fact]
    public async Task Scan_cancellation_handler_propagates_an_unrelated_cancellation_when_the_scan_is_still_active()
    {
        using var scanCancellation = new CancellationTokenSource();
        using var unrelatedCancellation = new CancellationTokenSource();
        unrelatedCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ScanCancellationHandler.TryRunAsync(
                _ => Task.FromCanceled(unrelatedCancellation.Token),
                scanCancellation.Token));
    }

    [Fact]
    public void Successful_publication_runs_the_staged_ui_commit_callback()
    {
        var store = new CompletedScanStateStore();
        var coordinator = new CompletedScanPublicationCoordinator(store);
        var snapshot = Snapshot("next-app", "Ctrl+F2");
        var uiCommitRan = false;

        coordinator.CreateAndPublish(
            () => snapshot,
            CancellationToken.None,
            () => uiCommitRan = true);

        Assert.True(uiCommitRan);
        Assert.Same(snapshot, store.Current);
    }

    [Fact]
    public void Canceled_publication_does_not_run_the_staged_ui_commit_callback()
    {
        var store = new CompletedScanStateStore();
        var coordinator = new CompletedScanPublicationCoordinator(store);
        var uiCommitRan = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            coordinator.CreateAndPublish(
                () => Snapshot("next-app", "Ctrl+F2"),
                cancellation.Token,
                () => uiCommitRan = true));

        Assert.False(uiCommitRan);
        Assert.Same(CompletedScanExportSnapshot.Empty, store.Current);
    }

    [Fact]
    public async Task Export_reads_the_complete_prior_scan_while_configuration_delays_refresh_publication()
    {
        var prior = Snapshot("prior-app", "Ctrl+F1");
        var next = Snapshot("next-app", "Ctrl+F2");
        var store = new CompletedScanStateStore(prior);
        var coordinator = new CompletedScanPublicationCoordinator(store);
        var configurationComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task RefreshAsync()
        {
            await configurationComplete.Task;
            coordinator.CreateAndPublish(() => next, TestContext.Current.CancellationToken);
        }

        var refresh = RefreshAsync();

        // Export occurs while configuration, attribution, and UI projection are
        // still incomplete, so the next snapshot is not visible yet.
        Assert.Same(prior, store.Current);
        Assert.Equal("prior-app", Assert.Single(store.Current.Applications).ApplicationId);
        Assert.Equal("Ctrl+F1", Assert.Single(store.Current.ProbeResults).Gesture.ToString());

        configurationComplete.SetResult();
        await refresh;

        Assert.Same(next, store.Current);
        Assert.Equal("next-app", Assert.Single(store.Current.Applications).ApplicationId);
        Assert.Equal("Ctrl+F2", Assert.Single(store.Current.ProbeResults).Gesture.ToString());
    }

    [Fact]
    public async Task Current_retains_last_completed_scan_when_refresh_fails_before_publish()
    {
        var prior = Snapshot("prior-app", "Ctrl+F1");
        var store = new CompletedScanStateStore(prior);
        Task failedRefresh = Task.FromException(new InvalidOperationException("Configuration read failed."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failedRefresh);

        // A failed refresh never calls Publish.
        Assert.Same(prior, store.Current);
        Assert.Equal("prior-app", Assert.Single(store.Current.Applications).ApplicationId);
        Assert.Equal("Ctrl+F1", Assert.Single(store.Current.ProbeResults).Gesture.ToString());
    }

    [Fact]
    public async Task Current_retains_last_completed_scan_when_refresh_is_canceled_before_publish()
    {
        var prior = Snapshot("prior-app", "Ctrl+F1");
        var store = new CompletedScanStateStore(prior);
        var canceledRefreshSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        canceledRefreshSource.SetCanceled(TestContext.Current.CancellationToken);
        var canceledRefresh = canceledRefreshSource.Task;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledRefresh);

        // A canceled refresh never calls Publish.
        Assert.Same(prior, store.Current);
        Assert.Equal("prior-app", Assert.Single(store.Current.Applications).ApplicationId);
        Assert.Equal("Ctrl+F1", Assert.Single(store.Current.ProbeResults).Gesture.ToString());
    }

    [Fact]
    public async Task Commit_does_not_publish_when_cancellation_arrives_after_the_final_dependency()
    {
        var prior = Snapshot("prior-app", "Ctrl+F1");
        var next = Snapshot("next-app", "Ctrl+F2");
        var store = new CompletedScanStateStore(prior);
        var coordinator = new CompletedScanPublicationCoordinator(store);
        var finalDependencyComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalDependencyObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commitAllowed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();

        async Task RefreshAsync()
        {
            await finalDependencyComplete.Task;
            finalDependencyObserved.SetResult();
            await commitAllowed.Task;
            coordinator.CreateAndPublish(() => next, cancellation.Token);
        }

        var refresh = RefreshAsync();
        finalDependencyComplete.SetResult();
        await finalDependencyObserved.Task;
        cancellation.Cancel();
        commitAllowed.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);

        Assert.Same(prior, store.Current);
    }

    [Fact]
    public async Task Post_probe_cancellation_is_handled_without_publishing_a_partial_snapshot()
    {
        var prior = Snapshot("prior-app", "Ctrl+F1");
        var store = new CompletedScanStateStore(prior);
        var coordinator = new CompletedScanPublicationCoordinator(store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var completed = await ScanCancellationHandler.TryRunAsync(async token =>
        {
            await Task.FromCanceled(token);
            coordinator.CreateAndPublish(() => Snapshot("next-app", "Ctrl+F2"), cancellation.Token);
        }, cancellation.Token);

        Assert.False(completed);
        Assert.Same(prior, store.Current);
    }

    [Fact]
    public void Published_snapshot_does_not_expose_mutable_collection_instances()
    {
        var snapshot = Snapshot("prior-app", "Ctrl+F1");

        var applications = Assert.IsAssignableFrom<IList<DiagnosticApplication>>(snapshot.Applications);

        Assert.Throws<NotSupportedException>(() => applications[0] = new DiagnosticApplication(
            "replaced-app",
            "replaced-app",
            "sample.exe",
            "1.0",
            "Sample",
            "X64",
            "Standard",
            "Foreground",
            []));
        Assert.Equal("prior-app", Assert.Single(snapshot.Applications).ApplicationId);
    }

    private static CompletedScanExportSnapshot Snapshot(string applicationId, string gesture) =>
        CompletedScanExportSnapshot.Create(
            [new DiagnosticApplication(
                applicationId,
                applicationId,
                "sample.exe",
                "1.0",
                "Sample",
                "X64",
                "Standard",
                "Foreground",
                [])],
            [new HotkeyProbeResult(
                HotkeyGesture.Parse(gesture),
                HotkeyProbeAvailability.Occupied,
                HotkeyProbeMechanism.RegisterHotKeyProbe,
                HotkeyOwner.Unknown,
                DateTimeOffset.UtcNow)],
            []);

}
