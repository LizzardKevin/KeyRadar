using System.IO.Pipes;
using System.Text.Json;

namespace KeyRadar.Windows.Applications;

public sealed class NamedPipeElevatedScanTransport : IElevatedScanTransport
{
    public IElevatedScanReception Begin(ElevatedScanRequest request) => new Reception(request.PipeName);

    public static async Task SendAsync(ElevatedScanRequest request, IReadOnlyList<ProcessDescriptor> processes, CancellationToken cancellationToken)
    {
        await using var client = new NamedPipeClientStream(
            ".", request.PipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(ElevatedProcessSnapshot.Success(request.Nonce, processes));
        if (payload.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException("Elevated scan payload exceeds the protocol limit.");
        }

        await client.WriteAsync(BitConverter.GetBytes(payload.Length), cancellationToken).ConfigureAwait(false);
        await client.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await client.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private const int MaximumPayloadBytes = 4 * 1024 * 1024;

    private sealed class Reception : IElevatedScanReception
    {
        private readonly NamedPipeServerStream _server;

        public Reception(string pipeName)
        {
            _server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }

        public async Task<ElevatedProcessSnapshot> ReceiveAsync(CancellationToken cancellationToken)
        {
            await _server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            var lengthBuffer = new byte[sizeof(int)];
            await ReadExactlyAsync(_server, lengthBuffer, cancellationToken).ConfigureAwait(false);
            var length = BitConverter.ToInt32(lengthBuffer);
            if (length is <= 0 or > MaximumPayloadBytes)
            {
                throw new InvalidDataException("Elevated scan payload length is invalid.");
            }

            var payload = new byte[length];
            await ReadExactlyAsync(_server, payload, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ElevatedProcessSnapshot>(payload)
                ?? throw new InvalidDataException("Elevated scan payload is empty.");
        }

        public ValueTask DisposeAsync() => _server.DisposeAsync();

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
}
