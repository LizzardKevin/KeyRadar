using System.Diagnostics;
using System.Globalization;
using KeyRadar.Shortcuts;
using KeyRadar.Windows.Applications;

namespace KeyRadar.Windows.DeepConfirmation;

public sealed class NativeDeepConfirmationComponent : IDeepConfirmationComponent, IDisposable
{
    private readonly string _baseDirectory;
    private readonly List<Process> _hosts = [];
    private int _targetProcessId;
    private ProcessArchitecture _targetArchitecture;
    private bool _requiresElevation;
    private string? _sessionDirectory;

    public NativeDeepConfirmationComponent(string? baseDirectory = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
    }

    public int? LastObservedProcessId { get; private set; }

    public Task LoadAsync(int processId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        using var process = Process.GetProcessById(processId);
        var descriptor = ProcessMetadataReader.Read(process);
        _targetProcessId = processId;
        _targetArchitecture = descriptor.Architecture;
        _requiresElevation = descriptor.PrivilegeLevel == ProcessPrivilegeLevel.Elevated;
        LastObservedProcessId = null;

        foreach (var architecture in ArchitecturesToObserve())
        {
            EnsureNativeFilesExist(architecture);
        }

        return Task.CompletedTask;
    }

    public async Task<DeepConfirmationResult> WaitForTargetAsync(
        ShortcutGesture target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (_targetProcessId <= 0)
        {
            throw new InvalidOperationException("LoadAsync must be called before waiting for confirmation.");
        }

        if (!NativeHotkeyMapper.TryMap(target, out var virtualKey, out var modifiers))
        {
            return DeepConfirmationResult.Failed;
        }

        var deepConfirmationRoot = GetDeepConfirmationRoot();
        _sessionDirectory = Path.Combine(deepConfirmationRoot, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_sessionDirectory);

        var resultFiles = new List<string>();
        foreach (var architecture in ArchitecturesToObserve())
        {
            var resultFile = Path.Combine(_sessionDirectory, $"{architecture}.pid");
            resultFiles.Add(resultFile);
            _hosts.Add(StartHost(architecture, virtualKey, modifiers, timeout, resultFile));
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var resultFile in resultFiles)
            {
                var observedProcessId = TryReadProcessId(resultFile);
                if (observedProcessId is not > 0)
                {
                    continue;
                }

                LastObservedProcessId = observedProcessId;
                return observedProcessId == _targetProcessId
                    ? DeepConfirmationResult.Confirmed
                    : DeepConfirmationResult.Failed;
            }

            if (_hosts.All(host => host.HasExited))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
        }

        return DeepConfirmationResult.TimedOut;
    }

    public Task UnloadAsync(CancellationToken cancellationToken)
    {
        foreach (var host in _hosts)
        {
            try
            {
                if (!host.HasExited)
                {
                    host.Kill(entireProcessTree: true);
                    host.WaitForExit(milliseconds: 2_000);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // A host may exit between checks or an elevated host may already be closing.
            }
            finally
            {
                host.Dispose();
            }
        }

        _hosts.Clear();
        TryDeleteSessionDirectory();
        _targetProcessId = 0;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        UnloadAsync(CancellationToken.None).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private IEnumerable<ProcessArchitecture> ArchitecturesToObserve()
    {
        if (!_requiresElevation)
        {
            return [ProcessArchitecture.X64, ProcessArchitecture.X86];
        }

        return _targetArchitecture switch
        {
            ProcessArchitecture.X86 => [ProcessArchitecture.X86],
            _ => [ProcessArchitecture.X64],
        };
    }

    private Process StartHost(
        ProcessArchitecture architecture,
        uint virtualKey,
        uint modifiers,
        TimeSpan timeout,
        string resultFile)
    {
        var hostPath = HostPath(architecture);
        var startInfo = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = _requiresElevation,
            CreateNoWindow = !_requiresElevation,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = _baseDirectory,
        };
        if (_requiresElevation)
        {
            startInfo.Verb = "runas";
        }

        startInfo.ArgumentList.Add("--vk");
        startInfo.ArgumentList.Add(virtualKey.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--mod");
        startInfo.ArgumentList.Add(modifiers.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--timeout");
        startInfo.ArgumentList.Add(Math.Min(30_000, (int)timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--result");
        startInfo.ArgumentList.Add(resultFile);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("The native observation host could not start.");
    }

    private void EnsureNativeFilesExist(ProcessArchitecture architecture)
    {
        if (!File.Exists(HostPath(architecture)) || !File.Exists(LibraryPath(architecture)))
        {
            throw new FileNotFoundException($"The {architecture} deep-confirmation component is missing.");
        }
    }

    private string HostPath(ProcessArchitecture architecture) => Path.Combine(
        _baseDirectory,
        architecture == ProcessArchitecture.X86 ? "KeyRadar.Native.Host.x86.exe" : "KeyRadar.Native.Host.x64.exe");

    private string LibraryPath(ProcessArchitecture architecture) => Path.Combine(
        _baseDirectory,
        architecture == ProcessArchitecture.X86 ? "KeyRadar.Native.x86.dll" : "KeyRadar.Native.x64.dll");

    private static int? TryReadProcessId(string path)
    {
        try
        {
            return File.Exists(path) && int.TryParse(File.ReadAllText(path), NumberStyles.None, CultureInfo.InvariantCulture, out var processId)
                ? processId
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void TryDeleteSessionDirectory()
    {
        if (_sessionDirectory is null || !Directory.Exists(_sessionDirectory))
        {
            return;
        }

        var root = GetDeepConfirmationRoot() + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(_sessionDirectory);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.Delete(fullPath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A just-elevated host may still be releasing its result file.
        }

        _sessionDirectory = null;
    }

    private static string GetDeepConfirmationRoot() => Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KeyRadar",
        "deep-confirmation"));
}
