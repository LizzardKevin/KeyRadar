using System.Diagnostics;
using KeyRadar.Updater.Updates;

namespace KeyRadar.Updater;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            WaitForMainProcess(options);
            var result = FileUpdateTransaction.Apply(options.Source, options.Target, options.Backup);
            if (!result.Succeeded)
            {
                return result.RolledBack ? 2 : 3;
            }

            PrepareHealthMarker(options.HealthMarker);
            using var restartedProcess = Restart(options, includeHealthMarker: true);
            if (string.IsNullOrWhiteSpace(options.HealthMarker) || restartedProcess is null)
            {
                return 0;
            }

            var healthy = UpdateHealthMonitor.WaitForMarkerAsync(
                    options.HealthMarker,
                    TimeSpan.FromSeconds(15))
                .GetAwaiter()
                .GetResult();
            if (healthy)
            {
                return 0;
            }

            StopFailedProcess(restartedProcess);
            var rolledBack = FileUpdateTransaction.Rollback(options.Source, options.Target, options.Backup);
            if (rolledBack)
            {
                using var previousProcess = Restart(options, includeHealthMarker: false);
            }

            return rolledBack ? 4 : 5;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            DirectoryNotFoundException or
            InvalidDataException or
            FormatException or
            InvalidOperationException or
            IOException or
            System.ComponentModel.Win32Exception)
        {
            return 1;
        }
    }

    private static UpdaterOptions ParseOptions(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Updater arguments must be supplied as --name value pairs.");
            }

            values[args[index][2..]] = args[index + 1];
        }

        return new UpdaterOptions(
            Required(values, "source"),
            Required(values, "target"),
            Required(values, "backup"),
            values.TryGetValue("wait-pid", out var processId) ? int.Parse(processId) : null,
            values.GetValueOrDefault("restart"),
            values.GetValueOrDefault("health-marker"));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing --{name}.");

    private static void WaitForMainProcess(UpdaterOptions options)
    {
        if (options.WaitProcessId is not { } processId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(milliseconds: 30_000))
            {
                throw new InvalidOperationException("KeyRadar did not exit before the update timeout.");
            }
        }
        catch (ArgumentException)
        {
            // The main process already exited.
        }
    }

    private static Process? Restart(UpdaterOptions options, bool includeHealthMarker)
    {
        if (string.IsNullOrWhiteSpace(options.RestartExecutable))
        {
            return null;
        }

        var target = Path.GetFullPath(options.Target);
        var executable = Path.GetFullPath(Path.Combine(target, options.RestartExecutable));
        var targetPrefix = Path.EndsInDirectorySeparator(target) ? target : target + Path.DirectorySeparatorChar;
        if (!executable.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
        {
            throw new InvalidDataException("The restart executable is outside the application directory or missing.");
        }

        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (includeHealthMarker && !string.IsNullOrWhiteSpace(options.HealthMarker))
        {
            startInfo.Environment["KEYRADAR_UPDATE_HEALTH_MARKER"] = Path.GetFullPath(options.HealthMarker);
        }

        return Process.Start(startInfo);
    }

    private static void PrepareHealthMarker(string? markerPath)
    {
        if (string.IsNullOrWhiteSpace(markerPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(markerPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    private static void StopFailedProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(milliseconds: 5_000);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between checks.
        }
    }

    private sealed record UpdaterOptions(
        string Source,
        string Target,
        string Backup,
        int? WaitProcessId,
        string? RestartExecutable,
        string? HealthMarker);
}
