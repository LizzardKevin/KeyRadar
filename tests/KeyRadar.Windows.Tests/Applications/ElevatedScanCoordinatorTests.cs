using System.ComponentModel;
using System.Diagnostics;
using System.Xml.Linq;
using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.Tests.Applications;

public sealed class ElevatedScanCoordinatorTests
{
    [Fact]
    public async Task Successful_elevated_snapshot_supplements_the_matching_process_without_duplicates()
    {
        var launcher = new StubLauncher();
        var transport = new StubTransport(ElevatedProcessSnapshot.Success("nonce", [
            new ProcessDescriptor(42, "ShareX", "ShareX.exe", "2.0", "Publisher", ProcessArchitecture.X64, ProcessPrivilegeLevel.Elevated, "Company", "package", "microsoft-store")
        ]));
        var coordinator = new ElevatedScanCoordinator(launcher, transport, TimeSpan.FromSeconds(1));
        var normal = new[] { new ApplicationSnapshot(new ProcessDescriptor(42, "ShareX", "ShareX.exe", "1.0"), ApplicationPresence.Foreground) };

        var result = await coordinator.ScanAndMergeAsync(normal, CancellationToken.None);

        var merged = Assert.Single(result.Snapshots);
        Assert.Equal(ElevatedScanStatus.Succeeded, result.Status);
        Assert.Equal("1.0", merged.Process.Version);
        Assert.Equal("Publisher", merged.Process.Publisher);
        Assert.Equal(ProcessPrivilegeLevel.Elevated, merged.Process.PrivilegeLevel);
        Assert.Equal(ApplicationPresence.Foreground, merged.Presence);
    }

    [Fact]
    public void Elevated_snapshot_never_reintroduces_a_process_missing_from_the_limited_scan()
    {
        var normal = new[] { new ApplicationSnapshot(new ProcessDescriptor(42, "ShareX", "ShareX.exe"), ApplicationPresence.Foreground) };
        var elevated = new[]
        {
            new ProcessDescriptor(42, "ShareX", "ShareX.exe", PrivilegeLevel: ProcessPrivilegeLevel.Elevated),
            new ProcessDescriptor(99, "KeyRadar", "KeyRadar.exe", PrivilegeLevel: ProcessPrivilegeLevel.Elevated),
        };

        var merged = ApplicationSnapshotMerger.Merge(normal, elevated);

        var snapshot = Assert.Single(merged);
        Assert.Equal(42, snapshot.Process.Id);
        Assert.Equal(ProcessPrivilegeLevel.Elevated, snapshot.Process.PrivilegeLevel);
    }

    [Fact]
    public async Task Uac_cancellation_keeps_the_normal_snapshot_and_reports_limited_scan()
    {
        var coordinator = new ElevatedScanCoordinator(
            new ThrowingLauncher(new Win32Exception(1223)), new StubTransport(), TimeSpan.FromSeconds(1));
        var normal = new[] { new ApplicationSnapshot(new ProcessDescriptor(42, "ShareX", "ShareX.exe"), ApplicationPresence.Background) };

        var result = await coordinator.ScanAndMergeAsync(normal, CancellationToken.None);

        Assert.Equal(ElevatedScanStatus.UserDeclined, result.Status);
        Assert.Same(normal, result.Snapshots);
    }

    [Fact]
    public async Task Helper_start_failure_keeps_normal_results_and_reports_unavailable()
    {
        var coordinator = new ElevatedScanCoordinator(
            new ThrowingLauncher(new InvalidOperationException("missing helper")), new StubTransport(), TimeSpan.FromSeconds(1));
        var normal = new[] { new ApplicationSnapshot(new ProcessDescriptor(42, "ShareX", "ShareX.exe"), ApplicationPresence.Background) };

        var result = await coordinator.ScanAndMergeAsync(normal, CancellationToken.None);

        Assert.Equal(ElevatedScanStatus.Unavailable, result.Status);
        Assert.Same(normal, result.Snapshots);
    }

    [Fact]
    public async Task Transport_initialization_failure_keeps_normal_results_and_reports_unavailable()
    {
        var launcher = new CountingLauncher();
        var coordinator = new ElevatedScanCoordinator(
            launcher, new ThrowingTransport(new IOException("pipe unavailable")), TimeSpan.FromSeconds(1));
        var normal = new[] { new ApplicationSnapshot(new ProcessDescriptor(42, "ShareX", "ShareX.exe"), ApplicationPresence.Background) };

        var result = await coordinator.ScanAndMergeAsync(normal, CancellationToken.None);

        Assert.Equal(ElevatedScanStatus.Unavailable, result.Status);
        Assert.Same(normal, result.Snapshots);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task Pre_cancelled_scan_does_not_create_a_pipe_or_start_the_helper()
    {
        var launcher = new CountingLauncher();
        var transport = new CountingTransport();
        var coordinator = new ElevatedScanCoordinator(launcher, transport, TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ScanAndMergeAsync([], cancellation.Token));

        Assert.Equal(0, transport.BeginCount);
        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task Cancellation_after_pipe_setup_does_not_start_the_helper()
    {
        var launcher = new CountingLauncher();
        using var cancellation = new CancellationTokenSource();
        var coordinator = new ElevatedScanCoordinator(launcher, new CancellingBeginTransport(cancellation), TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ScanAndMergeAsync([], cancellation.Token));

        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public void Elevated_helper_request_has_a_bounded_operation_timeout()
    {
        var request = ElevatedScanRequest.Create(TimeSpan.FromSeconds(1));

        Assert.Equal(1000, request.OperationTimeoutMilliseconds);
    }

    [Fact]
    public void Cancelled_helper_enumeration_exits_before_reading_processes()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            SystemProcessSource.ReadAllowedProcesses(Environment.ProcessId, [], cancellation.Token));
    }

    [Fact]
    public void Elevated_helper_scans_only_the_parent_allowlist()
    {
        var helperSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "KeyRadar.ElevatedScanner", "Program.cs"));
        var processSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "KeyRadar.Windows", "Applications", "SystemProcessSource.cs"));

        Assert.Contains("ReadAllowedProcesses", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadCurrentSessionProcesses", helperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.GetProcesses()", processSource[processSource.IndexOf("ReadAllowedProcesses", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void Protocol_rejects_invalid_allowlists_before_the_helper_scans()
    {
        var wrongVersion = new ElevatedScanCommand(2, "nonce", 1000, [new ElevatedProcessTarget(4, null)]);
        var nonPositive = new ElevatedScanCommand(ElevatedScanProtocol.Version, "nonce", 1000, [new ElevatedProcessTarget(0, null)]);
        var duplicate = new ElevatedScanCommand(
            ElevatedScanProtocol.Version, "nonce", 1000, [new ElevatedProcessTarget(4, null), new ElevatedProcessTarget(4, null)]);
        var oversized = new ElevatedScanCommand(
            ElevatedScanProtocol.Version,
            "nonce",
            1000,
            Enumerable.Range(1, ElevatedScanProtocol.MaximumProcesses + 1)
                .Select(id => new ElevatedProcessTarget(id, null)).ToArray());

        Assert.False(ElevatedScanProtocol.TryValidateCommand(wrongVersion, "nonce", out _));
        Assert.False(ElevatedScanProtocol.TryValidateCommand(nonPositive, "nonce", out _));
        Assert.False(ElevatedScanProtocol.TryValidateCommand(duplicate, "nonce", out _));
        Assert.False(ElevatedScanProtocol.TryValidateCommand(oversized, "nonce", out _));
    }

    [Fact]
    public void Allowlist_excludes_KeyRadar_infrastructure_processes()
    {
        var snapshots = new[]
        {
            new ApplicationSnapshot(new ProcessDescriptor(1, "KeyRadar", "KeyRadar.exe"), ApplicationPresence.Background),
            new ApplicationSnapshot(new ProcessDescriptor(2, "KeyRadar.ElevatedScanner", "KeyRadar.ElevatedScanner.exe"), ApplicationPresence.Background),
            new ApplicationSnapshot(new ProcessDescriptor(3, "KeyRadar.Updater", "KeyRadar.Updater.exe"), ApplicationPresence.Background),
            new ApplicationSnapshot(new ProcessDescriptor(4, "KeyRadar.NativeHost.x64", "KeyRadar.NativeHost.x64.exe"), ApplicationPresence.Background),
            new ApplicationSnapshot(new ProcessDescriptor(5, "ShareX", "ShareX.exe"), ApplicationPresence.Background),
        };

        var allowlist = ElevatedScanProtocol.CreateAllowlist(snapshots);

        Assert.Collection(allowlist, target => Assert.Equal(5, target.Id));
    }

    [Fact]
    public void Snapshot_validation_rejects_a_pid_not_present_in_the_parent_allowlist()
    {
        var snapshot = ElevatedProcessSnapshot.Success("nonce", [new ProcessDescriptor(9, "other", "other.exe")]);

        Assert.False(ElevatedProcessSnapshotValidator.TryValidate(
            snapshot, "nonce", [new ElevatedProcessTarget(8, null)], out _));
    }

    [Fact]
    public async Task Parent_cancellation_sends_cooperative_cancel_before_best_effort_kill()
    {
        var transport = new CancelRecordingTransport();
        var launcher = new StubLauncher(new StubProcess(new Win32Exception(5)));
        using var cancellation = new CancellationTokenSource();
        var coordinator = new ElevatedScanCoordinator(launcher, transport, TimeSpan.FromSeconds(5));

        cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ScanAndMergeAsync([], cancellation.Token));

        Assert.True(transport.CancelSent);
        Assert.True(launcher.Process.Terminated);
    }

    [Fact]
    public async Task Duplex_pipe_cancel_triggers_the_helper_cancellation_token()
    {
        var request = ElevatedScanRequest.Create(TimeSpan.FromSeconds(1));
        var transport = new NamedPipeElevatedScanTransport();
        await using var reception = transport.Begin(request);
        var commandReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var helper = Task.Run(async () =>
        {
            await using var session = await NamedPipeElevatedScanTransport.ReceiveCommandAsync(request, CancellationToken.None);
            commandReceived.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, session.CancellationToken);
                return false;
            }
            catch (OperationCanceledException)
            {
                return true;
            }
        });

        using var receiveCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var receive = reception.ReceiveAsync([new ElevatedProcessTarget(7, null)], receiveCancellation.Token);
        await commandReceived.Task.WaitAsync(receiveCancellation.Token);
        await reception.CancelAsync(receiveCancellation.Token);

        Assert.True(await helper.WaitAsync(receiveCancellation.Token));
        await Assert.ThrowsAnyAsync<Exception>(async () => await receive);
    }

    [Fact]
    public async Task Cleanup_uses_one_two_second_budget_when_cross_integrity_kill_is_denied()
    {
        var launcher = new SlowLauncher();
        var coordinator = new ElevatedScanCoordinator(launcher, new SlowDisposeTransport(), TimeSpan.FromSeconds(1));
        var stopwatch = Stopwatch.StartNew();

        var result = await coordinator.ScanAndMergeAsync([], CancellationToken.None);

        stopwatch.Stop();
        Assert.Equal(ElevatedScanStatus.Succeeded, result.Status);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromMilliseconds(2400));
    }

    [Fact]
    public async Task Timeout_keeps_normal_results_and_terminates_only_the_started_helper()
    {
        var launcher = new StubLauncher();
        var coordinator = new ElevatedScanCoordinator(launcher, new NeverCompletingTransport(), TimeSpan.FromMilliseconds(25));
        var normal = new[] { new ApplicationSnapshot(new ProcessDescriptor(42, "ShareX", "ShareX.exe"), ApplicationPresence.Background) };

        var result = await coordinator.ScanAndMergeAsync(normal, CancellationToken.None);

        Assert.Equal(ElevatedScanStatus.TimedOut, result.Status);
        Assert.True(launcher.Process.Terminated);
    }

    [Fact]
    public async Task Cancellation_terminates_the_started_helper()
    {
        var launcher = new StubLauncher();
        using var cancellation = new CancellationTokenSource();
        var coordinator = new ElevatedScanCoordinator(launcher, new CancellingTransport(cancellation), TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ScanAndMergeAsync([], cancellation.Token));

        Assert.True(launcher.Process.Terminated);
    }

    [Fact]
    public async Task Cleanup_swallows_access_denied_when_terminating_the_started_helper()
    {
        var launcher = new StubLauncher(new StubProcess(new Win32Exception(5)));
        var coordinator = new ElevatedScanCoordinator(launcher, new StubTransport(), TimeSpan.FromSeconds(1));

        var result = await coordinator.ScanAndMergeAsync([], CancellationToken.None);

        Assert.Equal(ElevatedScanStatus.Succeeded, result.Status);
        Assert.True(launcher.Process.Terminated);
    }

    [Theory]
    [InlineData(0x1000u)]
    [InlineData(0x2000u)]
    public void Non_elevated_helper_integrity_levels_cannot_send_a_scan_payload(uint integrityRid)
    {
        Assert.False(ElevatedHelperPrivilege.IsHighIntegrity(integrityRid));
    }

    [Fact]
    public void Timeout_has_a_distinct_localized_limited_scan_status()
    {
        var text = ElevatedScanStatusMessages.For(ElevatedScanStatus.TimedOut);

        Assert.Equal("管理员扫描超时，已完成有限扫描", text.Chinese);
        Assert.Equal("Administrator scan timed out; limited scan completed", text.English);
    }

    [Theory]
    [InlineData("wrong-nonce")]
    [InlineData("")]
    public void Snapshot_validation_rejects_invalid_nonce(string nonce)
    {
        var snapshot = ElevatedProcessSnapshot.Success(nonce, [new ProcessDescriptor(1, "a", "a.exe")]);

        Assert.False(ElevatedProcessSnapshotValidator.TryValidate(snapshot, "expected", out _));
    }

    [Fact]
    public void Snapshot_validation_rejects_sensitive_or_invalid_process_data()
    {
        var snapshot = ElevatedProcessSnapshot.Success("expected", [new ProcessDescriptor(0, "a", "C:\\secret\\a.exe")]);

        Assert.False(ElevatedProcessSnapshotValidator.TryValidate(snapshot, "expected", out _));
    }

    [Fact]
    public void Main_application_manifest_remains_as_invoker()
    {
        var manifest = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "KeyRadar.App", "app.manifest"));

        Assert.Contains("level=\"asInvoker\"", manifest.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
        Assert.DoesNotContain("requireAdministrator", manifest.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
    }

    private static string RepositoryRoot() => Ancestors(AppContext.BaseDirectory)
        .First(path => File.Exists(Path.Combine(path, "KeyRadar.sln")));

    private static IEnumerable<string> Ancestors(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            yield return current.FullName;
        }
    }

    private sealed class StubLauncher(StubProcess? process = null) : IElevatedScanLauncher
    {
        public StubProcess Process { get; } = process ?? new();
        public IElevatedScanProcess Start(ElevatedScanRequest request) => Process;
    }

    private sealed class ThrowingLauncher(Exception exception) : IElevatedScanLauncher
    {
        public IElevatedScanProcess Start(ElevatedScanRequest request) => throw exception;
    }

    private sealed class StubProcess(Exception? terminateException = null) : IElevatedScanProcess
    {
        public bool HasExited => false;
        public bool Terminated { get; private set; }
        public void Terminate()
        {
            Terminated = true;
            if (terminateException is not null)
            {
                throw terminateException;
            }
        }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SlowProcess(Exception terminateException) : IElevatedScanProcess
    {
        public bool HasExited => false;
        public void Terminate() => throw terminateException;
        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.Delay(TimeSpan.FromMilliseconds(1400), cancellationToken);
    }

    private sealed class SlowLauncher : IElevatedScanLauncher
    {
        public IElevatedScanProcess Start(ElevatedScanRequest request) => new SlowProcess(new Win32Exception(5));
    }

    private sealed class CountingLauncher : IElevatedScanLauncher
    {
        public int StartCount { get; private set; }

        public IElevatedScanProcess Start(ElevatedScanRequest request)
        {
            StartCount++;
            return new StubProcess();
        }
    }

    private sealed class CountingTransport : IElevatedScanTransport
    {
        public int BeginCount { get; private set; }

        public IElevatedScanReception Begin(ElevatedScanRequest request)
        {
            BeginCount++;
            return new StubTransport().Begin(request);
        }
    }

    private sealed class ThrowingTransport(Exception exception) : IElevatedScanTransport
    {
        public IElevatedScanReception Begin(ElevatedScanRequest request) => throw exception;
    }

    private sealed class CancellingBeginTransport(CancellationTokenSource cancellation) : IElevatedScanTransport
    {
        public IElevatedScanReception Begin(ElevatedScanRequest request)
        {
            cancellation.Cancel();
            return new StubTransport().Begin(request);
        }
    }

    private sealed class StubTransport(ElevatedProcessSnapshot? response = null) : IElevatedScanTransport
    {
        public IElevatedScanReception Begin(ElevatedScanRequest request) => new Reception(
            response is { Nonce: "nonce" } configured
                ? configured with { Nonce = request.Nonce }
                : response ?? ElevatedProcessSnapshot.Success(request.Nonce, []));

        private sealed class Reception(ElevatedProcessSnapshot response) : IElevatedScanReception
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task CancelAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<ElevatedProcessSnapshot> ReceiveAsync(IReadOnlyList<ElevatedProcessTarget> allowlist, CancellationToken cancellationToken) => Task.FromResult(response);
        }
    }

    private sealed class NeverCompletingTransport : IElevatedScanTransport
    {
        public IElevatedScanReception Begin(ElevatedScanRequest request) => new Reception();

        private sealed class Reception : IElevatedScanReception
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task CancelAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public async Task<ElevatedProcessSnapshot> ReceiveAsync(IReadOnlyList<ElevatedProcessTarget> allowlist, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return ElevatedProcessSnapshot.Success(string.Empty, []);
            }
        }
    }

    private sealed class CancellingTransport(CancellationTokenSource cancellation) : IElevatedScanTransport
    {
        public IElevatedScanReception Begin(ElevatedScanRequest request) => new Reception(cancellation);

        private sealed class Reception(CancellationTokenSource cancellation) : IElevatedScanReception
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task CancelAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public async Task<ElevatedProcessSnapshot> ReceiveAsync(IReadOnlyList<ElevatedProcessTarget> allowlist, CancellationToken cancellationToken)
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return ElevatedProcessSnapshot.Success(string.Empty, []);
            }
        }
    }

    private sealed class SlowDisposeTransport : IElevatedScanTransport
    {
        public IElevatedScanReception Begin(ElevatedScanRequest request) => new Reception(request);

        private sealed class Reception(ElevatedScanRequest request) : IElevatedScanReception
        {
            public async ValueTask DisposeAsync() => await Task.Delay(TimeSpan.FromMilliseconds(1400));
            public Task CancelAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<ElevatedProcessSnapshot> ReceiveAsync(IReadOnlyList<ElevatedProcessTarget> allowlist, CancellationToken cancellationToken) =>
                Task.FromResult(ElevatedProcessSnapshot.Success(request.Nonce, []));
        }
    }

    private sealed class CancelRecordingTransport : IElevatedScanTransport
    {
        public bool CancelSent { get; private set; }

        public IElevatedScanReception Begin(ElevatedScanRequest request) => new Reception(this);

        private sealed class Reception(CancelRecordingTransport owner) : IElevatedScanReception
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task CancelAsync(CancellationToken cancellationToken)
            {
                owner.CancelSent = true;
                return Task.CompletedTask;
            }

            public async Task<ElevatedProcessSnapshot> ReceiveAsync(IReadOnlyList<ElevatedProcessTarget> allowlist, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return ElevatedProcessSnapshot.Success(string.Empty, []);
            }
        }
    }
}
