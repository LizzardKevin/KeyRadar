using System.ComponentModel;
using System.Diagnostics;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.DeepConfirmation;

/// <summary>
/// Runs a short native trace for the curated unknown occupied entries selected after a scan.
/// The native host watches only WM_HOTKEY for the requested gesture; ordinary input is not
/// observed or persisted.
/// </summary>
public interface IHotkeyOwnerTracer
{
    Task<OwnerTraceResult> TraceAsync(HotkeyOwnerTraceTarget target, CancellationToken cancellationToken);
}

public sealed class NativeHotkeyOwnerTracer : IHotkeyOwnerTracer
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan TraceTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CleanupBudget = TimeSpan.FromMilliseconds(500);
    private readonly string _applicationDirectory;
    private readonly IOwnerTraceHostLauncher _hostLauncher;
    private readonly Func<HotkeyProbeSafety> _safetyCheck;

    public NativeHotkeyOwnerTracer(string? applicationDirectory = null)
        : this(
            applicationDirectory,
            new ProcessOwnerTraceHostLauncher(),
            () => new WindowsHotkeyProbeSafetyGate().Check())
    {
    }

    internal NativeHotkeyOwnerTracer(
        string? applicationDirectory,
        IOwnerTraceHostLauncher hostLauncher,
        Func<HotkeyProbeSafety> safetyCheck)
    {
        _applicationDirectory = applicationDirectory ?? AppContext.BaseDirectory;
        _hostLauncher = hostLauncher ?? throw new ArgumentNullException(nameof(hostLauncher));
        _safetyCheck = safetyCheck ?? throw new ArgumentNullException(nameof(safetyCheck));
    }

    public async Task<OwnerTraceResult> TraceAsync(HotkeyOwnerTraceTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_safetyCheck() != HotkeyProbeSafety.Safe)
        {
            return new OwnerTraceResult(OwnerTraceStatus.Failed);
        }
        if (!NativeHotkeyMapper.TryMap(target.Gesture, out var virtualKey, out var modifiers))
        {
            return new OwnerTraceResult(OwnerTraceStatus.NativeSupportUnavailable);
        }

        var x64Host = Path.Combine(_applicationDirectory, "KeyRadar.Native.Host.x64.exe");
        var x86Host = Path.Combine(_applicationDirectory, "KeyRadar.Native.Host.x86.exe");
        if (!File.Exists(x64Host) && !File.Exists(x86Host))
        {
            return new OwnerTraceResult(OwnerTraceStatus.NativeSupportUnavailable);
        }

        // Stopwatch uses QueryPerformanceCounter on Windows. Combining it with this
        // process's PID and tick count gives each short-lived trace a non-reusable
        // mapping nonce; shared-memory IPC still is not trusted against same-user code.
        var nonce = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{(ulong)Stopwatch.GetTimestamp():x16}{Environment.ProcessId:x8}{(uint)Environment.TickCount64:x8}");
        var traceId = nonce;
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "KeyRadar", "owner-trace");
        Directory.CreateDirectory(temporaryDirectory);
        var readyPath = Path.Combine(temporaryDirectory, $"{traceId}.ready");
        var resultPath = Path.Combine(temporaryDirectory, $"{traceId}.result");
        IOwnerTraceHost? passiveX86 = null;
        IOwnerTraceHost? active = null;
        IOwnerTraceHost? traceOwner = null;
        try
        {
            if (File.Exists(x64Host) && File.Exists(x86Host))
            {
                passiveX86 = _hostLauncher.Start(x86Host, virtualKey, modifiers, resultPath, readyPath, nonce, createTrace: true, triggerInput: false);
                if (!await WaitForFileAsync(readyPath, passiveX86, ReadyTimeout, cancellationToken))
                {
                    await StopAndDisposeAsync(passiveX86);
                    passiveX86 = null;
                }
                else
                {
                    traceOwner = passiveX86;
                }
            }

            var activeHost = File.Exists(x64Host) ? x64Host : x86Host;
            active = _hostLauncher.Start(activeHost, virtualKey, modifiers, resultPath, readyPath: null, nonce,
                createTrace: traceOwner is null, triggerInput: true);
            traceOwner ??= active;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TraceTimeout + TimeSpan.FromSeconds(1));
            try
            {
                await active.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new OwnerTraceResult(OwnerTraceStatus.Canceled);
            }
            catch (OperationCanceledException)
            {
                return new OwnerTraceResult(OwnerTraceStatus.TimedOut);
            }

            if (active.ExitCode != 0 || !File.Exists(resultPath) ||
                !NativeOwnerTraceHostProtocol.TryParseResult(
                    await File.ReadAllTextAsync(resultPath, CancellationToken.None),
                    new OwnerTraceHostIdentity(
                        nonce,
                        virtualKey,
                        modifiers,
                        traceOwner.ProcessId,
                        traceOwner.StartFileTimeUtc), out var result))
            {
                return new OwnerTraceResult(OwnerTraceStatus.Failed);
            }

            if (result.Status != OwnerTraceStatus.Detected || result.ProcessId is not int processId ||
                result.ThreadId is not > 0 || !TryReadTraceOwner(processId, out var processName))
            {
                return result.Status == OwnerTraceStatus.Detected ? new OwnerTraceResult(OwnerTraceStatus.Failed) : result;
            }

            return result with
            {
                ProcessName = processName,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new OwnerTraceResult(OwnerTraceStatus.Canceled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return new OwnerTraceResult(OwnerTraceStatus.Failed);
        }
        finally
        {
            // Kill both helpers first, then spend one bounded cleanup budget waiting for
            // their WH_GETMESSAGE hooks to leave the desktop before removing trace files.
            await StopAndDisposeAsync(active, passiveX86);
            TryDelete(readyPath);
            TryDelete(resultPath);
        }
    }

    private static async Task<bool> WaitForFileAsync(string path, IOwnerTraceHost process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var expiresAt = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (!File.Exists(path) && !process.HasExited && Stopwatch.GetTimestamp() < expiresAt)
        {
            await Task.Delay(25, cancellationToken);
        }
        return File.Exists(path);
    }

    private static bool TryReadTraceOwner(int processId, out string? processName)
    {
        processName = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.SessionId != Process.GetCurrentProcess().SessionId ||
                process.ProcessName.StartsWith("KeyRadar.Native.Host", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            processName = process.ProcessName;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static async Task StopAndDisposeAsync(params IOwnerTraceHost?[] processes)
    {
        var activeProcesses = processes.Where(process => process is not null).Cast<IOwnerTraceHost>().ToArray();
        foreach (var process in activeProcesses)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                // The short-lived helper already exited.
            }
        }

        using var cleanupCancellation = new CancellationTokenSource(CleanupBudget);
        try
        {
            await Task.WhenAll(activeProcesses.Select(process => WaitForExitAsync(process, cleanupCancellation.Token)));
        }
        finally
        {
            foreach (var process in activeProcesses)
            {
                process.Dispose();
            }
        }
    }

    private static async Task WaitForExitAsync(IOwnerTraceHost process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A host which cannot be reaped within the shared cleanup budget has already
            // received Kill; release our handle and leave Windows to finish teardown.
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // The helper exited while its process state was being queried.
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

internal interface IOwnerTraceHost : IDisposable
{
    int ProcessId { get; }
    long StartFileTimeUtc { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void Kill(bool entireProcessTree = true);
}

internal interface IOwnerTraceHostLauncher
{
    IOwnerTraceHost Start(string host, uint virtualKey, uint modifiers, string resultPath, string? readyPath,
        string nonce, bool createTrace, bool triggerInput);
}

internal sealed class ProcessOwnerTraceHostLauncher : IOwnerTraceHostLauncher
{
    public IOwnerTraceHost Start(string host, uint virtualKey, uint modifiers, string resultPath, string? readyPath,
        string nonce, bool createTrace, bool triggerInput)
    {
        var arguments = $"--trace --vk {virtualKey} --mod {modifiers} --nonce {nonce} --timeout-ms {(int)TraceTimeout.TotalMilliseconds} --result \"{resultPath}\"";
        if (readyPath is not null) arguments += $" --ready \"{readyPath}\"";
        if (createTrace) arguments += " --create-trace";
        if (!triggerInput) arguments += " --no-input";
        var process = Process.Start(new ProcessStartInfo(host, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        }) ?? throw new Win32Exception("Unable to start the native owner-trace host.");
        return new ProcessOwnerTraceHost(process);
    }

    private static TimeSpan TraceTimeout => TimeSpan.FromSeconds(3);
}

internal sealed class ProcessOwnerTraceHost : IOwnerTraceHost
{
    private readonly Process _process;

    public ProcessOwnerTraceHost(Process process)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        StartFileTimeUtc = _process.StartTime.ToFileTimeUtc();
    }

    public int ProcessId => _process.Id;
    public long StartFileTimeUtc { get; }
    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;
    public Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);
    public void Kill(bool entireProcessTree = true) => _process.Kill(entireProcessTree);
    public void Dispose() => _process.Dispose();
}
