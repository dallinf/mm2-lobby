using System.Net;
using System.Net.Sockets;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// Sends and receives announcements over UDP. It binds to 0.0.0.0 so that a
/// directed broadcast such as 10.147.17.255 arrives. PeerTable decides which
/// senders to trust.
/// </summary>
public sealed class UdpPeerTransport : IPeerTransport
{
    private static readonly TimeSpan SocketErrorPause = TimeSpan.FromMilliseconds(50);

    private UdpClient? _client;
    private CancellationTokenSource? _loop;
    private Task? _receive;

    public event Action<byte[], IPAddress>? DatagramReceived;

    public Task StartAsync(int port, CancellationToken ct)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("The transport has already started.");
        }

        var client = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            client.Client.SetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            client.EnableBroadcast = true;
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;

        _loop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receive = ReceiveLoopAsync(client, _loop.Token);
        return Task.CompletedTask;
    }

    public async Task SendAsync(
        byte[] payload, IPAddress destination, int port, CancellationToken ct)
    {
        var client = _client
            ?? throw new InvalidOperationException("The transport has not started.");

        await client.SendAsync(payload, new IPEndPoint(destination, port), ct);
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var received = await client.ReceiveAsync(ct);

                try
                {
                    DatagramReceived?.Invoke(received.Buffer, received.RemoteEndPoint.Address);
                }
                catch (Exception)
                {
                    // A subscriber must not throw. If one does, the transport
                    // ignores the error, because one bad handler must not stop
                    // discovery for the whole session. Mm2Lobby.Core has no
                    // logging, so the error is lost.
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // One bad datagram must not end the loop. The pause stops a
                // persistent error, such as WSAECONNRESET on Windows, from
                // spinning the loop. Waiting on the loop token keeps shutdown
                // immediate. A throw inside a catch block does not reach the
                // sibling catch blocks, so this wait handles its own cancel.
                try
                {
                    await Task.Delay(SocketErrorPause, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _loop?.Cancel();
        _client?.Dispose();

        if (_receive is not null)
        {
            try
            {
                await _receive;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _loop?.Dispose();
        _loop = null;
        _client = null;
        _receive = null;
    }
}
