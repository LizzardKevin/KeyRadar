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
    var remaining = request.DeadlineUtc - DateTimeOffset.UtcNow;
    using var deadline = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    var processes = SystemProcessSource.ReadCurrentSessionProcesses(Environment.ProcessId, deadline.Token);
    await NamedPipeElevatedScanTransport.SendAsync(request, processes, deadline.Token).ConfigureAwait(false);
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
    if (arguments.Length != 6 || arguments[0] != "--pipe" || arguments[2] != "--nonce" || arguments[4] != "--deadline" ||
        string.IsNullOrWhiteSpace(arguments[1]) || string.IsNullOrWhiteSpace(arguments[3]) ||
        !arguments[1].StartsWith("KeyRadar.ElevatedScan.", StringComparison.Ordinal) ||
        arguments[1].Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '.')) ||
        arguments[3].Length != 64 || arguments[3].Any(character => !Uri.IsHexDigit(character)) ||
        !long.TryParse(arguments[5], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var deadlineTicks))
    {
        return null;
    }

    try
    {
        return new ElevatedScanRequest(arguments[1], arguments[3], new DateTimeOffset(new DateTime(deadlineTicks, DateTimeKind.Utc)));
    }
    catch (ArgumentOutOfRangeException)
    {
        return null;
    }
}
