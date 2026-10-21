using System.Net;
using System.Net.Sockets;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// This class sends and receives announcements over UDP. It binds the socket to
/// 0.0.0.0. A directed broadcast, for example 10.147.17.255, therefore arrives.
/// PeerTable decides which senders the app trusts.
/// </summary>
public sealed class UdpPeerTransport : IPeerTransport
{
    private static readonly TimeSpan SocketErrorPause = TimeSpan.FromMilliseconds(50);

    private UdpClient? _client;
    private CancellationTokenSource? _loop;
    private Task? _receive;
    private bool _disposed;

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
            // The app does not set ReuseAddress. The bind to 0.0.0.0 does not
            // need that option. The option also lets a second copy of the app
            // take the same port, and the app reports nothing. Without the
            // option, the bind in the second copy fails. Then the window can
            // tell the player that MM2 Lobby already runs.
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
        // DisposeAsync sets _client to null. Without this check, a send after
        // the disposal reports that the transport did not start.
        ObjectDisposedException.ThrowIf(_disposed, this);

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
                    // A subscriber must not throw. This catch ignores an
                    // exception from a subscriber, so one faulty subscriber
                    // cannot stop discovery. Mm2Lobby.Core has no logger. This
                    // catch discards the exception.
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
                // One bad datagram must not end the loop. The pause stops an
                // exception that repeats, for example WSAECONNRESET on Windows,
                // from making the loop run without a break. The pause uses the
                // loop token, so the shutdown is immediate. An exception inside
                // a catch block does not reach the other catch blocks of the
                // same try. So this pause catches its own cancellation.
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
        _disposed = true;
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
                // The receive task ends with this exception on shutdown.
            }
        }

        _loop?.Dispose();
        _loop = null;
        _client = null;
        _receive = null;
    }
}
