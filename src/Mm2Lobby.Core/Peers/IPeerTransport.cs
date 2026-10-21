using System.Net;

namespace Mm2Lobby.Core.Peers;

public interface IPeerTransport : IAsyncDisposable
{
    /// <summary>
    /// The transport raises this event in the receive loop, once for each
    /// datagram. A subscriber must not throw. The transport ignores an
    /// exception from a subscriber, so one faulty subscriber cannot stop
    /// discovery.
    /// </summary>
    /// <remarks>
    /// The transport supports one subscriber. If the code attaches a second
    /// subscriber, and the first subscriber throws, the second subscriber loses
    /// that datagram.
    /// </remarks>
    event Action<byte[], IPAddress>? DatagramReceived;

    Task StartAsync(int port, CancellationToken ct);

    Task SendAsync(byte[] payload, IPAddress destination, int port, CancellationToken ct);
}
