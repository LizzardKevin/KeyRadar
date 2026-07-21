using KeyRadar.Windows.Applications;

var request = ParseArguments(args);
if (request is null)
{
    return 2;
}

if (!ElevatedHelperPrivilege.IsCurrentProcessHighIntegrity())
{
    return 5;
}

try
{
    await using var session = await NamedPipeElevatedScanTransport.ReceiveCommandAsync(request, CancellationToken.None).ConfigureAwait(false);
    var processes = SystemProcessSource.ReadAllowedProcesses(
        Environment.ProcessId,
        session.Command.Processes,
        session.CancellationToken);
    await NamedPipeElevatedScanTransport.SendSnapshotAsync(
        session.Stream,
        ElevatedProcessSnapshot.Success(request.Nonce, processes),
        session.CancellationToken).ConfigureAwait(false);
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
    if (arguments.Length != 6 || arguments[0] != "--pipe" || arguments[2] != "--nonce" || arguments[4] != "--timeout-ms" ||
        string.IsNullOrWhiteSpace(arguments[1]) || string.IsNullOrWhiteSpace(arguments[3]) ||
        !arguments[1].StartsWith("KeyRadar.ElevatedScan.", StringComparison.Ordinal) ||
        arguments[1].Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '.')) ||
        arguments[3].Length != 64 || arguments[3].Any(character => !Uri.IsHexDigit(character)) ||
        !int.TryParse(arguments[5], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var timeoutMilliseconds) ||
        timeoutMilliseconds is <= 0 or > ElevatedScanProtocol.MaximumOperationTimeoutMilliseconds)
    {
        return null;
    }

    return new ElevatedScanRequest(arguments[1], arguments[3], timeoutMilliseconds);
}
