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

            Restart(options);
            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            DirectoryNotFoundException or
            InvalidDataException or
            FormatException or
            InvalidOperationException)
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
            values.GetValueOrDefault("restart"));
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

    private static void Restart(UpdaterOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RestartExecutable))
        {
            return;
        }

        var target = Path.GetFullPath(options.Target);
        var executable = Path.GetFullPath(Path.Combine(target, options.RestartExecutable));
        var targetPrefix = Path.EndsInDirectorySeparator(target) ? target : target + Path.DirectorySeparatorChar;
        if (!executable.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
        {
            throw new InvalidDataException("The restart executable is outside the application directory or missing.");
        }

        _ = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    private sealed record UpdaterOptions(
        string Source,
        string Target,
        string Backup,
        int? WaitProcessId,
        string? RestartExecutable);
}
