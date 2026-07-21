using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace KeyRadar.Windows.Applications;

public enum ElevatedScanStatus
{
    Succeeded,
    UserDeclined,
    Unavailable,
    TimedOut,
}

public sealed record ElevatedScanResult(
    IReadOnlyList<ApplicationSnapshot> Snapshots,
    ElevatedScanStatus Status);

public sealed record ElevatedScanRequest(string PipeName, string Nonce, int OperationTimeoutMilliseconds)
{
    public static ElevatedScanRequest Create(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        return new ElevatedScanRequest(
            $"KeyRadar.ElevatedScan.{Guid.NewGuid():N}",
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            checked((int)Math.Ceiling(timeout.TotalMilliseconds)));
    }
}

public sealed record ElevatedProcessTarget(int Id, long? StartTimeUtcTicks);

public sealed record ElevatedScanCommand(
    int Version,
    string Nonce,
    int OperationTimeoutMilliseconds,
    IReadOnlyList<ElevatedProcessTarget> Processes);

public sealed record ElevatedScanControl(int Version, string Command);

public static class ElevatedScanProtocol
{
    public const int Version = 1;
    public const int MaximumProcesses = 16384;
    public const int MaximumOperationTimeoutMilliseconds = 12000;
    public const string CancelCommand = "cancel";

    public static IReadOnlyList<ElevatedProcessTarget> CreateAllowlist(IReadOnlyList<ApplicationSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var targets = new List<ElevatedProcessTarget>();
        var seen = new HashSet<int>();
        foreach (var snapshot in snapshots)
        {
            var process = snapshot.Process;
            if (process.Id <= 0 || process.Id == Environment.ProcessId || !seen.Add(process.Id) ||
                IsKeyRadarInfrastructure(process.Name) || !NeedsElevatedMetadata(process))
            {
                continue;
            }

            targets.Add(new ElevatedProcessTarget(process.Id, TryReadStartTimeUtcTicks(process.Id)));
        }

        return targets;
    }

    public static bool NeedsElevatedMetadata(ProcessDescriptor process)
    {
        ArgumentNullException.ThrowIfNull(process);

        // A null version, publisher, or package can be a valid fact (for example an
        // unsigned Win32 app or a non-packaged process). Only retry fields the normal
        // scan explicitly could not read, or enum values that remain unknown.
        return process.UnavailableMetadata != ProcessMetadataUnavailable.None ||
            string.IsNullOrWhiteSpace(process.ExecutableName) ||
            process.Architecture == ProcessArchitecture.Unknown ||
            process.PrivilegeLevel == ProcessPrivilegeLevel.Unknown;
    }

    public static bool TryValidateCommand(
        ElevatedScanCommand? command,
        string expectedNonce,
        out IReadOnlyList<ElevatedProcessTarget> processes)
    {
        processes = [];
        if (command is null || command.Version != Version ||
            !FixedTimeEquals(command.Nonce, expectedNonce) ||
            command.OperationTimeoutMilliseconds is <= 0 or > MaximumOperationTimeoutMilliseconds ||
            command.Processes is null || command.Processes.Count > MaximumProcesses ||
            command.Processes.Any(target => target is null || target.Id <= 0 || !IsValidStartTime(target.StartTimeUtcTicks)) ||
            command.Processes.Select(target => target.Id).Distinct().Count() != command.Processes.Count)
        {
            return false;
        }

        processes = command.Processes.ToArray();
        return true;
    }

    public static bool IsCancel(ElevatedScanControl? control) =>
        control is { Version: Version, Command: CancelCommand };

    private static bool IsKeyRadarInfrastructure(string? processName) =>
        string.Equals(processName, "KeyRadar", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "KeyRadar.ElevatedScanner", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "KeyRadar.Updater", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(processName, "KeyRadar.NativeHost", StringComparison.OrdinalIgnoreCase) ||
        processName?.StartsWith("KeyRadar.NativeHost.", StringComparison.OrdinalIgnoreCase) == true;

    private static long? TryReadStartTimeUtcTicks(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static bool IsValidStartTime(long? value) =>
        value is null || (value.Value > DateTime.MinValue.Ticks && value.Value <= DateTime.MaxValue.Ticks);

    private static bool FixedTimeEquals(string? left, string? right) => CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.UTF8.GetBytes(left ?? string.Empty),
        System.Text.Encoding.UTF8.GetBytes(right ?? string.Empty));
}

public interface IElevatedScanProcess
{
    int ProcessId { get; }
    bool HasExited { get; }
    void Terminate();
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public interface IElevatedScanLauncher
{
    IElevatedScanProcess Start(ElevatedScanRequest request);
}

public interface IElevatedScanTransport
{
    IElevatedScanReception Begin(ElevatedScanRequest request);
}

public interface IElevatedScanReception : IAsyncDisposable
{
    Task<ElevatedProcessSnapshot> ReceiveAsync(
        int expectedHelperProcessId,
        IReadOnlyList<ElevatedProcessTarget> allowlist,
        CancellationToken cancellationToken);
    Task CancelAsync(CancellationToken cancellationToken);
}

public sealed class ElevatedScanCoordinator(
    IElevatedScanLauncher launcher,
    IElevatedScanTransport transport,
    TimeSpan timeout)
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(2);

    public async Task<ElevatedScanResult> ScanAndMergeAsync(
        IReadOnlyList<ApplicationSnapshot> normalSnapshots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(normalSnapshots);
        cancellationToken.ThrowIfCancellationRequested();
        var request = ElevatedScanRequest.Create(timeout);
        var allowlist = ElevatedScanProtocol.CreateAllowlist(normalSnapshots);
        IElevatedScanReception? reception = null;
        IElevatedScanProcess? process = null;

        try
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                reception = transport.Begin(request);
            }
            catch (Exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.Unavailable);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                process = launcher.Start(request);
                if (process.ProcessId <= 0)
                {
                    return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.Unavailable);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.UserDeclined);
            }
            catch (Exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.Unavailable);
            }

            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCancellation.Token);
            ElevatedProcessSnapshot snapshot;
            try
            {
                snapshot = await reception.ReceiveAsync(process.ProcessId, allowlist, linkedCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.TimedOut);
            }
            catch (Exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.Unavailable);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!ElevatedProcessSnapshotValidator.TryValidate(snapshot, request.Nonce, allowlist, out var elevatedProcesses))
            {
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.Unavailable);
            }

            return new ElevatedScanResult(
                ApplicationSnapshotMerger.Merge(normalSnapshots, elevatedProcesses),
                ElevatedScanStatus.Succeeded);
        }
        finally
        {
            using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
            if (reception is not null)
            {
                try
                {
                    await reception.CancelAsync(cleanupCancellation.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }

                try
                {
                    await reception.DisposeAsync().AsTask().WaitAsync(cleanupCancellation.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Terminate();
                    }
                }
                catch (Exception)
                {
                }

                try
                {
                    await process.WaitForExitAsync(cleanupCancellation.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}

public static class ApplicationSnapshotMerger
{
    public static IReadOnlyList<ApplicationSnapshot> Merge(
        IReadOnlyList<ApplicationSnapshot> normalSnapshots,
        IReadOnlyList<ProcessDescriptor> elevatedProcesses)
    {
        ArgumentNullException.ThrowIfNull(normalSnapshots);
        ArgumentNullException.ThrowIfNull(elevatedProcesses);
        var elevatedById = elevatedProcesses
            .GroupBy(process => process.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var merged = normalSnapshots
            .Select(snapshot => elevatedById.TryGetValue(snapshot.Process.Id, out var elevated)
                ? snapshot with { Process = Merge(snapshot.Process, elevated) }
                : snapshot)
            .ToList();
        return merged;
    }

    private static ProcessDescriptor Merge(ProcessDescriptor normal, ProcessDescriptor elevated) => normal with
    {
        Version = Prefer(normal.Version, elevated.Version),
        Publisher = Prefer(normal.Publisher, elevated.Publisher),
        Architecture = normal.Architecture == ProcessArchitecture.Unknown ? elevated.Architecture : normal.Architecture,
        PrivilegeLevel = normal.PrivilegeLevel == ProcessPrivilegeLevel.Unknown ? elevated.PrivilegeLevel : normal.PrivilegeLevel,
        CompanyName = Prefer(normal.CompanyName, elevated.CompanyName),
        PackageFamilyName = Prefer(normal.PackageFamilyName, elevated.PackageFamilyName),
        Distribution = Prefer(normal.Distribution, elevated.Distribution),
        UnavailableMetadata = normal.UnavailableMetadata & elevated.UnavailableMetadata,
    };

    private static string? Prefer(string? current, string? elevated) =>
        string.IsNullOrWhiteSpace(current) ? elevated : current;
}

public sealed record ElevatedScanStatusMessage(string Chinese, string English);

public static class ElevatedScanStatusMessages
{
    public static ElevatedScanStatusMessage For(ElevatedScanStatus status) => status switch
    {
        ElevatedScanStatus.Succeeded => new("扫描完成", "Scan complete"),
        ElevatedScanStatus.UserDeclined => new("管理员扫描未授权，已完成有限扫描", "Administrator scan not authorized; limited scan completed"),
        ElevatedScanStatus.TimedOut => new("管理员扫描超时，已完成有限扫描", "Administrator scan timed out; limited scan completed"),
        _ => new("管理员扫描不可用，已完成有限扫描", "Administrator scan unavailable; limited scan completed"),
    };
}

public sealed record ElevatedProcessSnapshot(string Nonce, IReadOnlyList<ProcessDescriptor> Processes)
{
    public static ElevatedProcessSnapshot Success(string nonce, IReadOnlyList<ProcessDescriptor> processes) => new(nonce, processes);
}

public static class ElevatedProcessSnapshotValidator
{
    public static bool TryValidate(
        ElevatedProcessSnapshot? snapshot,
        string expectedNonce,
        out IReadOnlyList<ProcessDescriptor> processes)
    {
        processes = [];
        if (snapshot is null || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(snapshot.Nonce ?? string.Empty),
                System.Text.Encoding.UTF8.GetBytes(expectedNonce)) ||
            snapshot.Processes is null || snapshot.Processes.Count > ElevatedScanProtocol.MaximumProcesses)
        {
            return false;
        }

        if (snapshot.Processes.Any(process => process is null || !IsSafe(process)) ||
            snapshot.Processes.Select(process => process.Id).Distinct().Count() != snapshot.Processes.Count)
        {
            return false;
        }

        processes = snapshot.Processes.ToArray();
        return true;
    }

    public static bool TryValidate(
        ElevatedProcessSnapshot? snapshot,
        string expectedNonce,
        IReadOnlyList<ElevatedProcessTarget> allowlist,
        out IReadOnlyList<ProcessDescriptor> processes)
    {
        if (!TryValidate(snapshot, expectedNonce, out processes))
        {
            return false;
        }

        var allowedById = allowlist.ToDictionary(target => target.Id);
        if (processes.Any(process => !allowedById.ContainsKey(process.Id)))
        {
            processes = [];
            return false;
        }

        return true;
    }

    private static bool IsSafe(ProcessDescriptor process) =>
        process.Id > 0 &&
        IsName(process.Name, 260) &&
        IsFileName(process.ExecutableName) &&
        IsText(process.Version, 100) &&
        IsText(process.Publisher, 160) &&
        IsText(process.CompanyName, 160) &&
        IsText(process.PackageFamilyName, 260) &&
        IsText(process.Distribution, 64) &&
        Enum.IsDefined(process.Architecture) &&
        Enum.IsDefined(process.PrivilegeLevel) &&
        (process.UnavailableMetadata & ~AllMetadataFields) == ProcessMetadataUnavailable.None;

    private const ProcessMetadataUnavailable AllMetadataFields =
        ProcessMetadataUnavailable.ExecutableIdentity |
        ProcessMetadataUnavailable.Version |
        ProcessMetadataUnavailable.PublisherOrCompany |
        ProcessMetadataUnavailable.Architecture |
        ProcessMetadataUnavailable.PrivilegeLevel |
        ProcessMetadataUnavailable.PackageOrDistribution;

    private static bool IsName(string? value, int maximumLength) =>
        IsText(value, maximumLength) && !string.IsNullOrWhiteSpace(value);

    private static bool IsFileName(string? value) =>
        IsName(value, 260) && value!.IndexOfAny(['\\', '/', ':']) < 0;

    private static bool IsText(string? value, int maximumLength) =>
        value is null || (value.Length <= maximumLength && value.All(character => !char.IsControl(character)));
}
