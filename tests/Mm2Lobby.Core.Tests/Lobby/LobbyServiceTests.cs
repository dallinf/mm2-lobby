using System.Net;
using Microsoft.Extensions.Time.Testing;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Lobby;
using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Tests.Lobby;

public class LobbyServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"mm2lobby-{Guid.NewGuid():N}");

    private readonly FakeZeroTierClient _zeroTier = new();
    private readonly FakePeerTransport _transport = new();
    private DateTimeOffset _now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    private ConfigStore Store => new(Path.Combine(_folder, "config.json"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed class BlockingZeroTierClient : IZeroTierClient
    {
        private readonly FakeZeroTierClient _inner = new();

        public TaskCompletionSource Open { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StatusCalls { get; private set; }

        public async Task<ZeroTierStatus?> GetStatusAsync(CancellationToken ct)
        {
            StatusCalls++;
            await Open.Task;
            return await _inner.GetStatusAsync(ct);
        }

        public Task<ZeroTierNetwork?> GetNetworkAsync(string networkId, CancellationToken ct)
            => _inner.GetNetworkAsync(networkId, ct);

        public Task<IReadOnlyDictionary<string, LinkQuality>> GetLinkQualityAsync(
            CancellationToken ct)
            => _inner.GetLinkQualityAsync(ct);
    }

    private sealed class ScriptedZeroTierClient : IZeroTierClient
    {
        private readonly FakeZeroTierClient _inner = new();
        private readonly object _gate = new();
        private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = new();

        public Func<int, Exception?> FailOn { get; set; } = _ => null;

        public int StatusCalls { get; private set; }

        public Task WhenCalled(int count)
        {
            lock (_gate)
            {
                if (StatusCalls >= count) return Task.CompletedTask;

                var reached = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, reached));
                return reached.Task;
            }
        }

        public Task<ZeroTierStatus?> GetStatusAsync(CancellationToken ct)
        {
            int call;
            lock (_gate)
            {
                call = ++StatusCalls;
                foreach (var waiter in _waiters.Where(w => w.Count <= call))
                {
                    waiter.Reached.TrySetResult();
                }
            }

            var failure = FailOn(call);
            if (failure is not null) throw failure;

            return _inner.GetStatusAsync(ct);
        }

        public Task<ZeroTierNetwork?> GetNetworkAsync(string networkId, CancellationToken ct)
            => _inner.GetNetworkAsync(networkId, ct);

        public Task<IReadOnlyDictionary<string, LinkQuality>> GetLinkQualityAsync(
            CancellationToken ct)
            => _inner.GetLinkQualityAsync(ct);
    }

    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private LobbyService Build(
        IZeroTierClient? zeroTier = null, TimeProvider? timeProvider = null)
    {
        zeroTier ??= _zeroTier;
        var store = Store;
        store.Save(new AppConfig
        {
            DisplayName = "dallin",
            ZeroTierNetworkId = "0123456789abcdef",
        });

        var table = new PeerTable(LobbyService.PeerExpiry, () => _now);
        return new LobbyService(
            zeroTier, _transport, table, new PeerDirectory(zeroTier, table),
            store, "1.0.0", () => _now, timeProvider);
    }

    private static Announcement Decode(byte[] payload)
    {
        var parsed = Announcement.TryParse(payload);
        Assert.NotNull(parsed);
        return parsed;
    }

    [Fact]
    public async Task RunCycleAsync_broadcasts_this_app_to_the_subnet_broadcast_address()
    {
        await using var service = Build();

        await service.RunCycleAsync(default);

        var sent = Assert.Single(_transport.Sent);
        Assert.Equal(IPAddress.Parse("10.147.17.255"), sent.Destination);
        Assert.Equal(LobbyService.Port, sent.Port);

        var announcement = Decode(sent.Payload);
        Assert.Equal("dallin", announcement.Player);
        Assert.Equal("a09acf0233", announcement.NodeId);
        Assert.Equal("10.147.17.42", announcement.Address);
        Assert.Equal("1.0.0", announcement.AppVersion);
        Assert.False(announcement.Hosting);
    }

    [Fact]
    public async Task StartHosting_puts_the_host_flag_and_the_start_time_on_the_announcement()
    {
        await using var service = Build();
        service.StartHosting();

        await service.RunCycleAsync(default);

        var announcement = Decode(Assert.Single(_transport.Sent).Payload);
        Assert.True(service.IsHosting);
        Assert.True(announcement.Hosting);
        Assert.Equal(_now, announcement.StartedAt);
    }

    [Fact]
    public async Task StopHosting_clears_the_host_flag()
    {
        await using var service = Build();
        service.StartHosting();
        service.StopHosting();

        await service.RunCycleAsync(default);

        Assert.False(service.IsHosting);
        Assert.False(Decode(Assert.Single(_transport.Sent).Payload).Hosting);
    }

    [Fact]
    public async Task RunCycleAsync_sends_nothing_when_the_machine_has_not_joined_the_network()
    {
        _zeroTier.Network = new ZeroTierNetwork(
            "0123456789abcdef", "REQUESTING_CONFIGURATION", null, 0);
        await using var service = Build();

        await service.RunCycleAsync(default);

        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task RunCycleAsync_sends_nothing_when_the_ZeroTier_service_is_absent()
    {
        _zeroTier.Status = null;
        await using var service = Build();

        await service.RunCycleAsync(default);

        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task RunCycleAsync_raises_PeersChanged_with_the_peers_it_found()
    {
        await using var service = Build();
        await service.StartAsync(default);
        _transport.Deliver(
            new Announcement
            {
                NodeId = "bb11223344", Player = "chris", Address = "10.147.17.9",
            }.ToBytes(),
            IPAddress.Parse("10.147.17.9"));
        IReadOnlyList<Peer>? seen = null;
        service.PeersChanged += peers => seen = peers;

        await service.RunCycleAsync(default);

        Assert.NotNull(seen);
        var peer = Assert.Single(seen);
        Assert.Equal("bb11223344", peer.NodeId);
        Assert.Equal("chris", peer.DisplayName);
    }

    [Fact]
    public async Task A_delivered_announcement_reaches_the_peer_list()
    {
        await using var service = Build();
        await service.StartAsync(default);

        _transport.Deliver(
            new Announcement
            {
                AppVersion = "1.0.0",
                NodeId = "bb11223344",
                Player = "chris",
                Address = "10.147.17.9",
                Hosting = true,
            }.ToBytes(),
            IPAddress.Parse("10.147.17.9"));

        await service.RunCycleAsync(default);

        var host = service.CurrentHost;
        Assert.NotNull(host);
        Assert.Equal("chris", host.DisplayName);
        Assert.Equal(IPAddress.Parse("10.147.17.9"), host.Address);
    }

    [Fact]
    public async Task A_delivered_announcement_from_outside_the_subnet_is_ignored()
    {
        await using var service = Build();
        await service.StartAsync(default);

        _transport.Deliver(
            new Announcement
            {
                NodeId = "bb11223344", Player = "chris", Address = "192.168.1.9",
            }.ToBytes(),
            IPAddress.Parse("192.168.1.9"));

        await service.RunCycleAsync(default);

        Assert.Empty(service.Peers);
    }

    [Fact]
    public async Task RunCycleAsync_remembers_the_name_of_a_peer_it_heard()
    {
        await using var service = Build();
        await service.StartAsync(default);

        _transport.Deliver(
            new Announcement
            {
                NodeId = "bb11223344", Player = "chris", Address = "10.147.17.9",
            }.ToBytes(),
            IPAddress.Parse("10.147.17.9"));

        await service.RunCycleAsync(default);

        Assert.Equal("chris", Store.Load().RememberedNames["bb11223344"]);
    }

    [Fact]
    public async Task StartAsync_starts_the_transport_on_the_lobby_port()
    {
        await using var service = Build();

        await service.StartAsync(default);

        Assert.Equal(LobbyService.Port, _transport.StartedPort);
    }

    [Fact]
    public async Task MyAddress_reports_this_machine_on_the_virtual_network()
    {
        await using var service = Build();

        await service.RunCycleAsync(default);

        Assert.Equal("10.147.17.42", service.MyAddress);
    }

    [Fact]
    public async Task DisposeAsync_disposes_the_transport()
    {
        var service = Build();
        await service.StartAsync(default);

        await service.DisposeAsync();

        Assert.True(_transport.Disposed);
    }

    [Fact]
    public async Task DisposeAsync_called_twice_does_not_throw()
    {
        var service = Build();
        await service.StartAsync(default);

        await service.DisposeAsync();

        await service.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_called_twice_throws_and_leaves_one_subscriber()
    {
        await using var service = Build();
        await service.StartAsync(default);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(default));

        Assert.Equal(1, _transport.SubscriberCount);
    }

    [Fact]
    public async Task RunCycleAsync_raises_PeersChanged_when_the_name_save_fails_and_saves_on_the_next_cycle()
    {
        await using var service = Build();
        await service.StartAsync(default);
        _transport.Deliver(
            new Announcement
            {
                NodeId = "bb11223344", Player = "chris", Address = "10.147.17.9",
            }.ToBytes(),
            IPAddress.Parse("10.147.17.9"));
        IReadOnlyList<Peer>? seen = null;
        service.PeersChanged += peers => seen = peers;
        var configPath = Path.Combine(_folder, "config.json");

        File.SetAttributes(configPath, FileAttributes.ReadOnly);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => service.RunCycleAsync(default));
        }
        finally
        {
            File.SetAttributes(configPath, FileAttributes.Normal);
        }

        Assert.NotNull(seen);
        Assert.Equal("chris", Assert.Single(seen).DisplayName);
        Assert.DoesNotContain("bb11223344", Store.Load().RememberedNames.Keys);

        await service.RunCycleAsync(default);

        Assert.Equal("chris", Store.Load().RememberedNames["bb11223344"]);
    }

    [Fact]
    public async Task RunCycleAsync_does_not_start_a_second_cycle_while_one_is_running()
    {
        var client = new BlockingZeroTierClient();
        await using var service = Build(client);

        var first = service.RunCycleAsync(default);
        var second = service.RunCycleAsync(default);

        Assert.Equal(1, client.StatusCalls);

        client.Open.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, client.StatusCalls);
    }

    [Fact]
    public async Task RunCycleAsync_saves_the_names_when_a_PeersChanged_subscriber_throws()
    {
        await using var service = Build();
        await service.StartAsync(default);
        _transport.Deliver(
            new Announcement
            {
                NodeId = "bb11223344", Player = "chris", Address = "10.147.17.9",
            }.ToBytes(),
            IPAddress.Parse("10.147.17.9"));
        service.PeersChanged += _ => throw new InvalidOperationException("bad handler");

        await service.RunCycleAsync(default);

        Assert.Equal("chris", Store.Load().RememberedNames["bb11223344"]);
    }

    [Fact]
    public async Task The_pump_waits_for_the_first_tick_before_it_runs_another_cycle()
    {
        var client = new ScriptedZeroTierClient();
        var time = new FakeTimeProvider();
        await using var service = Build(client, time);

        await service.StartAsync(default);

        Assert.Equal(1, client.StatusCalls);

        time.Advance(LobbyService.CyclePeriod);
        await client.WhenCalled(2).WaitAsync(WaitLimit);

        Assert.Equal(2, client.StatusCalls);
    }

    [Fact]
    public async Task The_pump_runs_the_next_cycle_after_a_cycle_throws()
    {
        var client = new ScriptedZeroTierClient
        {
            FailOn = call => call == 2 ? new IOException("disk") : null,
        };
        var time = new FakeTimeProvider();
        await using var service = Build(client, time);
        await service.StartAsync(default);

        time.Advance(LobbyService.CyclePeriod);
        await client.WhenCalled(2).WaitAsync(WaitLimit);
        time.Advance(LobbyService.CyclePeriod);

        await client.WhenCalled(3).WaitAsync(WaitLimit);
    }

    [Fact]
    public async Task The_pump_runs_the_next_cycle_after_a_cancel_it_did_not_ask_for()
    {
        var client = new ScriptedZeroTierClient
        {
            FailOn = call => call == 2 ? new OperationCanceledException("not ours") : null,
        };
        var time = new FakeTimeProvider();
        await using var service = Build(client, time);
        await service.StartAsync(default);

        time.Advance(LobbyService.CyclePeriod);
        await client.WhenCalled(2).WaitAsync(WaitLimit);
        time.Advance(LobbyService.CyclePeriod);

        await client.WhenCalled(3).WaitAsync(WaitLimit);
    }
}
