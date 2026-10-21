using System.Net;
using Mm2Lobby.Core.Peers;

namespace Mm2Lobby.Core.Tests.Fakes;

public sealed class FakePeerTransport : IPeerTransport
{
    public List<(byte[] Payload, IPAddress Destination, int Port)> Sent { get; } = new();

    public int? StartedPort { get; private set; }

    public bool Disposed { get; private set; }

    public event Action<byte[], IPAddress>? DatagramReceived;

    public Task StartAsync(int port, CancellationToken ct)
    {
        StartedPort = port;
        return Task.CompletedTask;
    }

    public Task SendAsync(byte[] payload, IPAddress destination, int port, CancellationToken ct)
    {
        Sent.Add((payload, destination, port));
        return Task.CompletedTask;
    }

    /// <summary>Simulates an arriving datagram.</summary>
    public void Deliver(byte[] payload, IPAddress source)
        => DatagramReceived?.Invoke(payload, source);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
