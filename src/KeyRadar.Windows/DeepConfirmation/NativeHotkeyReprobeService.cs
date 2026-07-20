using System.Diagnostics;
using System.Globalization;
using KeyRadar.Hotkeys;
using KeyRadar.Windows.Hotkeys;

namespace KeyRadar.Windows.DeepConfirmation;

public enum NativeProbeArchitecture
{
    X64,
    X86,
}

public sealed record NativeHotkeyProbeResult(
    NativeProbeArchitecture Architecture,
    HotkeyProbeAvailability Availability,
    int? Win32ErrorCode);

public sealed class NativeHotkeyReprobeService(string? baseDirectory = null)
{
    private readonly string _baseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);

    public async Task<IReadOnlyList<NativeHotkeyProbeResult>> ProbeAsync(
        HotkeyGesture target,
        CancellationToken cancellationToken)
    {
        if (!NativeHotkeyMapper.TryMap(target, out var virtualKey, out var modifiers))
        {
            return [];
        }

        var componentRoot = GetComponentRoot();
        var sessionDirectory = Path.Combine(componentRoot, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(sessionDirectory);
        try
        {
            var results = new List<NativeHotkeyProbeResult>();
            foreach (var (architecture, suffix) in new[]
                     {
                         (NativeProbeArchitecture.X64, "x64"),
                         (NativeProbeArchitecture.X86, "x86"),
                     })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hostPath = Path.Combine(_baseDirectory, $"KeyRadar.Native.Host.{suffix}.exe");
                var libraryPath = Path.Combine(_baseDirectory, $"KeyRadar.Native.{suffix}.dll");
                if (!File.Exists(hostPath) || !File.Exists(libraryPath))
                {
                    continue;
                }

                var resultPath = Path.Combine(sessionDirectory, $"{suffix}.result");
                var startInfo = new ProcessStartInfo(hostPath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = _baseDirectory,
                };
                startInfo.ArgumentList.Add("--vk");
                startInfo.ArgumentList.Add(virtualKey.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add("--mod");
                startInfo.ArgumentList.Add(modifiers.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add("--result");
                startInfo.ArgumentList.Add(resultPath);

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    continue;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    TryTerminate(process);
                    results.Add(new NativeHotkeyProbeResult(
                        architecture,
                        HotkeyProbeAvailability.ProbeError,
                        null));
                    continue;
                }

                if (process.ExitCode == 0 &&
                    File.Exists(resultPath) &&
                    TryParseResult(await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false), out var availability, out var errorCode))
                {
                    results.Add(new NativeHotkeyProbeResult(architecture, availability, errorCode));
                }
                else
                {
                    results.Add(new NativeHotkeyProbeResult(
                        architecture,
                        HotkeyProbeAvailability.ProbeError,
                        process.ExitCode));
                }
            }

            return results;
        }
        finally
        {
            TryDeleteSessionDirectory(sessionDirectory, componentRoot);
        }
    }

    public static bool TryParseResult(
        string value,
        out HotkeyProbeAvailability availability,
        out int? win32ErrorCode)
    {
        availability = HotkeyProbeAvailability.ProbeError;
        win32ErrorCode = null;
        var parts = value.Trim().Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var status) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var errorCode))
        {
            return false;
        }

        availability = status switch
        {
            0 => HotkeyProbeAvailability.AvailableAtScanTime,
            1 => HotkeyProbeAvailability.Occupied,
            2 => HotkeyProbeAvailability.SystemReserved,
            3 => HotkeyProbeAvailability.ProbeError,
            _ => HotkeyProbeAvailability.ProbeError,
        };
        if (status is < 0 or > 3)
        {
            return false;
        }

        win32ErrorCode = errorCode == 0 ? null : errorCode;
        return true;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2_000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The one-shot helper may have exited between checks.
        }
    }

    private static void TryDeleteSessionDirectory(string sessionDirectory, string componentRoot)
    {
        var rootPrefix = Path.GetFullPath(componentRoot) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(sessionDirectory);
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A just-terminated helper may still be releasing its result file.
        }
    }

    private static string GetComponentRoot() => Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KeyRadar",
        "deep-confirmation"));
}
