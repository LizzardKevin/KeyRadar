using System.Diagnostics;
using System.Globalization;

namespace KeyRadar.Windows.Applications;

public sealed class ProcessElevatedScanLauncher(string helperPath) : IElevatedScanLauncher
{
    public const string HelperFileName = "KeyRadar.ElevatedScanner.exe";

    public static ProcessElevatedScanLauncher FromApplicationDirectory() => new(
        Path.Combine(AppContext.BaseDirectory, HelperFileName));

    public IElevatedScanProcess Start(ElevatedScanRequest request)
    {
        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException("The elevated scan helper is unavailable.", helperPath);
        }

        var process = Process.Start(new ProcessStartInfo
        {
            // Windows owns the UAC prompt. The operation clock starts only after
            // the helper connects and receives its parent-authorized allowlist.
            FileName = helperPath,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"--pipe {request.PipeName} --nonce {request.Nonce} --timeout-ms {request.OperationTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture)}",
            WindowStyle = ProcessWindowStyle.Hidden,
        }) ?? throw new InvalidOperationException("Windows did not start the elevated scan helper.");
        return new StartedProcess(process);
    }

    private sealed class StartedProcess(Process process) : IElevatedScanProcess
    {
        public bool HasExited => process.HasExited;

        public void Terminate()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
    }
}
