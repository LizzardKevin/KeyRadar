using KeyRadar.Hotkeys;
using KeyRadar.Windows.DeepConfirmation;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.Tests.DeepConfirmation;

public sealed class NativeHotkeyOwnerTracerTests
{
    [Fact]
    public async Task TraceAsync_cancellation_kills_and_disposes_the_active_host()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), "KeyRadar.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(applicationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(applicationDirectory, "KeyRadar.Native.Host.x64.exe"),
            "stub",
            TestContext.Current.CancellationToken);
        var host = new RecordingHost();
        var launcher = new RecordingLauncher(host);
        using var cancellation = new CancellationTokenSource();

        try
        {
            var tracer = new NativeHotkeyOwnerTracer(applicationDirectory, launcher, () => HotkeyProbeSafety.Safe);
            var trace = tracer.TraceAsync(new HotkeyOwnerTraceTarget(HotkeyGesture.Parse("Ctrl+Alt+K")), cancellation.Token);
            await launcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            cancellation.Cancel();

            var result = await trace;

            Assert.Equal(OwnerTraceStatus.Canceled, result.Status);
            Assert.Equal(1, host.KillCount);
            Assert.True(host.Disposed);
        }
        finally
        {
            Directory.Delete(applicationDirectory, recursive: true);
        }
    }

    private sealed class RecordingLauncher(RecordingHost host) : IOwnerTraceHostLauncher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IOwnerTraceHost Start(string executablePath, uint virtualKey, uint modifiers, string resultPath, string? readyPath,
            string nonce, bool createTrace, bool triggerInput)
        {
            Started.SetResult();
            return host;
        }
    }

    private sealed class RecordingHost : IOwnerTraceHost
    {
        public int ProcessId => 900;
        public long StartFileTimeUtc => 12639456;
        public bool HasExited => false;
        public int ExitCode => throw new InvalidOperationException();
        public int KillCount { get; private set; }
        public bool Disposed { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public void Kill(bool entireProcessTree = true) => KillCount++;
        public void Dispose() => Disposed = true;
    }
}
