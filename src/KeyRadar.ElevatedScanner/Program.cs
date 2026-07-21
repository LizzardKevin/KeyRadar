using KeyRadar.Windows.Applications;

var request = ParseArguments(args);
if (request is null)
{
    return 2;
}

try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var processes = await Task.Run(
        () => SystemProcessSource.ReadCurrentSessionProcesses(Environment.ProcessId),
        timeout.Token).ConfigureAwait(false);
    await NamedPipeElevatedScanTransport.SendAsync(request, processes, timeout.Token).ConfigureAwait(false);
    return 0;
}
catch (OperationCanceledException)
{
    return 3;
}
catch (Exception)
{
    return 4;
}

static ElevatedScanRequest? ParseArguments(string[] arguments)
{
    if (arguments.Length != 4 || arguments[0] != "--pipe" || arguments[2] != "--nonce" ||
        string.IsNullOrWhiteSpace(arguments[1]) || string.IsNullOrWhiteSpace(arguments[3]) ||
        !arguments[1].StartsWith("KeyRadar.ElevatedScan.", StringComparison.Ordinal) ||
        arguments[1].Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '.')) ||
        arguments[3].Length != 64 || arguments[3].Any(character => !Uri.IsHexDigit(character)))
    {
        return null;
    }

    return new ElevatedScanRequest(arguments[1], arguments[3]);
}
