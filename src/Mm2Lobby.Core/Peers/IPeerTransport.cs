using System.Net;

namespace Mm2Lobby.Core.Peers;

public interface IPeerTransport : IAsyncDisposable
{
    /// <summary>
    /// Raised on the receive thread, once per datagram. A subscriber must not
    /// throw. The transport ignores an exception from a subscriber, so that
    /// one bad handler cannot stop discovery for the whole session.
    /// </summary>
    event Action<byte[], IPAddress>? DatagramReceived;

    Task StartAsync(int port, CancellationToken ct);

    Task SendAsync(byte[] payload, IPAddress destination, int port, CancellationToken ct);
}
