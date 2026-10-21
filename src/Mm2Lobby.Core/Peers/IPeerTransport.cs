using System.Net;

namespace Mm2Lobby.Core.Peers;

public interface IPeerTransport : IAsyncDisposable
{
    event Action<byte[], IPAddress>? DatagramReceived;

    Task StartAsync(int port, CancellationToken ct);

    Task SendAsync(byte[] payload, IPAddress destination, int port, CancellationToken ct);
}
