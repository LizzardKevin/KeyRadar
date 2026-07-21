using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeyRadar.Windows.Applications;

public sealed partial class NamedPipeElevatedScanTransport : IElevatedScanTransport
{
    private readonly Func<SafePipeHandle, int?> _clientProcessIdReader;

    public NamedPipeElevatedScanTransport(Func<SafePipeHandle, int?>? clientProcessIdReader = null)
    {
        _clientProcessIdReader = clientProcessIdReader ?? GetClientProcessId;
    }

    public IElevatedScanReception Begin(ElevatedScanRequest request) => new Reception(request, _clientProcessIdReader);

    public static async Task<ElevatedScanHelperSession> ReceiveCommandAsync(
        ElevatedScanRequest request,
        CancellationToken cancellationToken)
    {
        using var connectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, connectionTimeout.Token);
        var client = new NamedPipeClientStream(
            ".", request.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(connectionCancellation.Token).ConfigureAwait(false);
        var command = await ReadAsync<ElevatedScanCommand>(client, cancellationToken).ConfigureAwait(false);
        if (!ElevatedScanProtocol.TryValidateCommand(command, request.Nonce, out _))
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw new InvalidDataException("Elevated scan command is invalid.");
        }

        var helperCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(command.OperationTimeoutMilliseconds));
        _ = WatchForCancellationAsync(client, helperCancellation);
        return new ElevatedScanHelperSession(client, command, helperCancellation);
    }

    public static Task SendSnapshotAsync(
        Stream stream,
        ElevatedProcessSnapshot snapshot,
        CancellationToken cancellationToken) => WriteAsync(stream, snapshot, cancellationToken);

    private const int MaximumPayloadBytes = 4 * 1024 * 1024;

    private sealed class Reception(
        ElevatedScanRequest request,
        Func<SafePipeHandle, int?> clientProcessIdReader) : IElevatedScanReception
    {
        private readonly NamedPipeServerStream _server = new(
            request.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        private bool _commandSent;

        public async Task<ElevatedProcessSnapshot> ReceiveAsync(
            int expectedHelperProcessId,
            IReadOnlyList<ElevatedProcessTarget> allowlist,
            CancellationToken cancellationToken)
        {
            await _server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (expectedHelperProcessId <= 0 || clientProcessIdReader(_server.SafePipeHandle) != expectedHelperProcessId)
            {
                throw new UnauthorizedAccessException("The named-pipe client is not the launched elevated helper.");
            }

            if (!_commandSent)
            {
                var command = new ElevatedScanCommand(
                    ElevatedScanProtocol.Version,
                    request.Nonce,
                    request.OperationTimeoutMilliseconds,
                    allowlist);
                await WriteAsync(_server, command, cancellationToken).ConfigureAwait(false);
                _commandSent = true;
            }

            return await ReadAsync<ElevatedProcessSnapshot>(_server, cancellationToken).ConfigureAwait(false);
        }

        public async Task CancelAsync(CancellationToken cancellationToken)
        {
            if (_server.IsConnected && _commandSent)
            {
                await WriteAsync(
                    _server,
                    new ElevatedScanControl(ElevatedScanProtocol.Version, ElevatedScanProtocol.CancelCommand),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        public ValueTask DisposeAsync() => _server.DisposeAsync();
    }

    private static int? GetClientProcessId(SafePipeHandle pipeHandle) =>
        GetNamedPipeClientProcessId(pipeHandle, out var processId) && processId <= int.MaxValue
            ? (int)processId
            : null;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle namedPipe, out uint clientProcessId);

    private static async Task WatchForCancellationAsync(Stream stream, CancellationTokenSource cancellation)
    {
        try
        {
            var control = await ReadAsync<ElevatedScanControl>(stream, CancellationToken.None).ConfigureAwait(false);
            if (ElevatedScanProtocol.IsCancel(control))
            {
                cancellation.Cancel();
            }
        }
        catch (Exception)
        {
            cancellation.Cancel();
        }
    }

    private static async Task WriteAsync<T>(Stream stream, T payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (bytes.Length is 0 or > MaximumPayloadBytes)
        {
            throw new InvalidDataException("Elevated scan payload exceeds the protocol limit.");
        }

        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false);
        var length = BitConverter.ToInt32(lengthBuffer);
        if (length is <= 0 or > MaximumPayloadBytes)
        {
            throw new InvalidDataException("Elevated scan payload length is invalid.");
        }

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload)
            ?? throw new InvalidDataException("Elevated scan payload is empty.");
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Elevated scan pipe closed before the payload was complete.");
            }

            offset += read;
        }
    }
}

public sealed class ElevatedScanHelperSession(
    NamedPipeClientStream stream,
    ElevatedScanCommand command,
    CancellationTokenSource cancellation) : IAsyncDisposable
{
    public Stream Stream => stream;
    public ElevatedScanCommand Command => command;
    public CancellationToken CancellationToken => cancellation.Token;

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        cancellation.Dispose();
        await stream.DisposeAsync().ConfigureAwait(false);
    }
}
