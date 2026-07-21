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

public sealed record ElevatedScanRequest(string PipeName, string Nonce)
{
    public static ElevatedScanRequest Create() => new(
        $"KeyRadar.ElevatedScan.{Guid.NewGuid():N}",
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
}

public interface IElevatedScanProcess
{
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
    Task<ElevatedProcessSnapshot> ReceiveAsync(CancellationToken cancellationToken);
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
        var request = ElevatedScanRequest.Create();
        await using var reception = transport.Begin(request);
        IElevatedScanProcess? process = null;

        try
        {
            try
            {
                process = launcher.Start(request);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.UserDeclined);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or FileNotFoundException)
            {
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.Unavailable);
            }

            using var timeoutCancellation = new CancellationTokenSource(timeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCancellation.Token);
            ElevatedProcessSnapshot snapshot;
            try
            {
                snapshot = await reception.ReceiveAsync(linkedCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.TimedOut);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.Unavailable);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!ElevatedProcessSnapshotValidator.TryValidate(snapshot, request.Nonce, out var elevatedProcesses))
            {
                return new ElevatedScanResult(normalSnapshots, ElevatedScanStatus.Unavailable);
            }

            return new ElevatedScanResult(
                ApplicationSnapshotMerger.Merge(normalSnapshots, elevatedProcesses),
                ElevatedScanStatus.Succeeded);
        }
        finally
        {
            if (process is not null && !process.HasExited)
            {
                process.Terminate();
                using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
                try
                {
                    await process.WaitForExitAsync(cleanupCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cleanupCancellation.IsCancellationRequested)
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
        var knownIds = normalSnapshots.Select(snapshot => snapshot.Process.Id).ToHashSet();
        merged.AddRange(elevatedProcesses
            .Where(process => !knownIds.Contains(process.Id))
            .Select(process => new ApplicationSnapshot(process, ApplicationPresence.Background)));
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
    };

    private static string? Prefer(string? current, string? elevated) =>
        string.IsNullOrWhiteSpace(current) ? elevated : current;
}

public sealed record ElevatedProcessSnapshot(string Nonce, IReadOnlyList<ProcessDescriptor> Processes)
{
    public static ElevatedProcessSnapshot Success(string nonce, IReadOnlyList<ProcessDescriptor> processes) => new(nonce, processes);
}

public static class ElevatedProcessSnapshotValidator
{
    private const int MaximumProcesses = 16384;

    public static bool TryValidate(
        ElevatedProcessSnapshot? snapshot,
        string expectedNonce,
        out IReadOnlyList<ProcessDescriptor> processes)
    {
        processes = [];
        if (snapshot is null || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(snapshot.Nonce ?? string.Empty),
                System.Text.Encoding.UTF8.GetBytes(expectedNonce)) ||
            snapshot.Processes is null || snapshot.Processes.Count > MaximumProcesses)
        {
            return false;
        }

        if (snapshot.Processes.Any(process => !IsSafe(process)) ||
            snapshot.Processes.Select(process => process.Id).Distinct().Count() != snapshot.Processes.Count)
        {
            return false;
        }

        processes = snapshot.Processes.ToArray();
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
        Enum.IsDefined(process.PrivilegeLevel);

    private static bool IsName(string? value, int maximumLength) =>
        IsText(value, maximumLength) && !string.IsNullOrWhiteSpace(value);

    private static bool IsFileName(string? value) =>
        IsName(value, 260) && value!.IndexOfAny(['\\', '/', ':']) < 0;

    private static bool IsText(string? value, int maximumLength) =>
        value is null || (value.Length <= maximumLength && value.All(character => !char.IsControl(character)));
}
