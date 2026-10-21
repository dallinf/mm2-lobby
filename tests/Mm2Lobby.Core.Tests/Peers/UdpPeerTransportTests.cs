using System.Net;
using System.Text;
using Mm2Lobby.Core.Peers;

namespace Mm2Lobby.Core.Tests.Peers;

public class UdpPeerTransportTests
{
    // Broadcast needs a real ZeroTier network, so it belongs to the manual
    // test. These tests prove the send and receive path over loopback.

    [Fact]
    public async Task A_datagram_sent_to_a_port_reaches_the_transport_bound_to_it()
    {
        var receiverPort = FreePort();
        var senderPort = FreePort();

        await using var receiver = new UdpPeerTransport();
        await using var sender = new UdpPeerTransport();

        var arrived = new TaskCompletionSource<(byte[] Payload, IPAddress Source)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.DatagramReceived += (payload, source) =>
            arrived.TrySetResult((payload, source));

        await receiver.StartAsync(receiverPort, default);
        await sender.StartAsync(senderPort, default);

        await sender.SendAsync(
            Encoding.UTF8.GetBytes("ping"), IPAddress.Loopback, receiverPort, default);

        var completed = await Task.WhenAny(arrived.Task, Task.Delay(5000));
        Assert.Same(arrived.Task, completed);

        var result = await arrived.Task;
        Assert.Equal("ping", Encoding.UTF8.GetString(result.Payload));
        Assert.Equal(IPAddress.Loopback, result.Source);
    }

    [Fact]
    public async Task SendAsync_before_StartAsync_is_an_error()
    {
        await using var transport = new UdpPeerTransport();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.SendAsync(new byte[] { 1 }, IPAddress.Loopback, 48200, default));
    }

    [Fact]
    public async Task StartAsync_twice_is_an_error()
    {
        await using var transport = new UdpPeerTransport();
        await transport.StartAsync(FreePort(), default);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.StartAsync(FreePort(), default));
    }

    [Fact]
    public async Task A_subscriber_that_throws_does_not_stop_the_receive_loop()
    {
        var receiverPort = FreePort();

        await using var receiver = new UdpPeerTransport();
        await using var sender = new UdpPeerTransport();

        var threw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        receiver.DatagramReceived += (payload, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                threw.TrySetResult();
                throw new InvalidOperationException("The subscriber failed.");
            }

            second.TrySetResult(Encoding.UTF8.GetString(payload));
        };

        await receiver.StartAsync(receiverPort, default);
        await sender.StartAsync(FreePort(), default);

        await sender.SendAsync(
            Encoding.UTF8.GetBytes("first"), IPAddress.Loopback, receiverPort, default);
        Assert.Same(threw.Task, await Task.WhenAny(threw.Task, Task.Delay(5000)));

        await sender.SendAsync(
            Encoding.UTF8.GetBytes("second"), IPAddress.Loopback, receiverPort, default);
        Assert.Same(second.Task, await Task.WhenAny(second.Task, Task.Delay(5000)));

        Assert.Equal("second", await second.Task);
    }

    private static int FreePort()
    {
        using var probe = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram,
            System.Net.Sockets.ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }
}
