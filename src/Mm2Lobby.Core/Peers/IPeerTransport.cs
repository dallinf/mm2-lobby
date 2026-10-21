using System.Net;

namespace Mm2Lobby.Core.Peers;

public interface IPeerTransport : IAsyncDisposable
{
    /// <summary>
    /// Raised on the receive thread, once per datagram. A subscriber must not
    /// throw. The transport ignores an exception from a subscriber, so that
    /// one bad handler cannot stop discovery for the whole session. The
    /// transport supports one subscriber. If there is more than one, a throw
    /// in the first makes the later subscribers miss that datagram.
    /// </summary>
    event Action<byte[], IPAddress>? DatagramReceived;

    Task StartAsync(int port, CancellationToken ct);

    Task SendAsync(byte[] payload, IPAddress destination, int port, CancellationToken ct);
}
