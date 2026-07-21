using System.ComponentModel;
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
        var coordinator = new ElevatedScanCoordinator(launcher, new NeverCompletingTransport(), TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ScanAndMergeAsync([], cancellation.Token));

        Assert.True(launcher.Process.Terminated);
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

    private sealed class StubLauncher : IElevatedScanLauncher
    {
        public StubProcess Process { get; } = new();
        public IElevatedScanProcess Start(ElevatedScanRequest request) => Process;
    }

    private sealed class ThrowingLauncher(Exception exception) : IElevatedScanLauncher
    {
        public IElevatedScanProcess Start(ElevatedScanRequest request) => throw exception;
    }

    private sealed class StubProcess : IElevatedScanProcess
    {
        public bool HasExited => false;
        public bool Terminated { get; private set; }
        public void Terminate() => Terminated = true;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
            public Task<ElevatedProcessSnapshot> ReceiveAsync(CancellationToken cancellationToken) => Task.FromResult(response);
        }
    }

    private sealed class NeverCompletingTransport : IElevatedScanTransport
    {
        public IElevatedScanReception Begin(ElevatedScanRequest request) => new Reception();

        private sealed class Reception : IElevatedScanReception
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public async Task<ElevatedProcessSnapshot> ReceiveAsync(CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return ElevatedProcessSnapshot.Success(string.Empty, []);
            }
        }
    }
}
