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

    // Build gives this configuration to the service. The test and the service
    // share this one object, so a test can change the object after the build.
    private AppConfig _config = new();

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

    /// <summary>
    /// This method builds the service. The service and the PeerDirectory take
    /// a client each. Then a test that scripts one client cannot lose the other
    /// half of its setup.
    /// </summary>
    private LobbyService Build(
        IZeroTierClient? zeroTier = null,
        IZeroTierClient? directoryZeroTier = null,
        TimeProvider? timeProvider = null)
    {
        zeroTier ??= _zeroTier;
        directoryZeroTier ??= _zeroTier;
        var store = Store;
        store.Save(new AppConfig
        {
            DisplayName = "dallin",
            ZeroTierNetworkId = "0123456789abcdef",
        });
        _config = store.Load();

        var table = new PeerTable(LobbyService.PeerExpiry, () => _now);
        return new LobbyService(
            zeroTier, _transport, table, new PeerDirectory(directoryZeroTier, table),
            store, _config, "1.0.0", () => _now, timeProvider);
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
        await service.StartAsync(default);

        await service.RunCycleAsync(default);

        // StartAsync runs one cycle, and this test runs a second one.
        Assert.Equal(2, _transport.Sent.Count);
        var sent = _transport.Sent[^1];
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
    public async Task AnnounceAsync_reads_the_display_name_one_time()
    {
        // The window owns the one AppConfig, and it writes the name while the
        // pump runs. With two reads, a name of 3 characters can replace a name
        // of 40 between the two, and the truncation then throws. The cost is
        // one skipped cycle and a confusing fault line.
        //
        // This test runs the real race: one thread writes the name while
        // another thread runs cycles. The two reads then take two values, and
        // the truncation throws inside the loop below. With the two reads in
        // place, 20000 cycles threw on every one of 10 runs, and each run
        // threw within 40 ms. With the one read, the loop never throws.
        var tooLong = new string('a', PeerTable.MaxPlayerNameLength + 8);
        var config = new AppConfig
        {
            DisplayName = "bob",
            ZeroTierNetworkId = "0123456789abcdef",
        };

        var table = new PeerTable(LobbyService.PeerExpiry, () => _now);
        await using var service = new LobbyService(
            _zeroTier, _transport, table, new PeerDirectory(_zeroTier, table),
            Store, config, "1.0.0", () => _now);

        await service.StartAsync(default);

        using var stop = new CancellationTokenSource();
        var writer = Task.Run(
            () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    config.DisplayName = tooLong;
                    config.DisplayName = "bob";
                }
            });

        try
        {
            for (var cycle = 0; cycle < 20000; cycle++)
            {
                await service.RunCycleAsync(default);
            }
        }
        finally
        {
            stop.Cancel();
            await writer;
        }
    }

    [Fact]
    public async Task StartHosting_puts_the_host_flag_and_the_start_time_on_the_announcement()
    {
        await using var service = Build();
        await service.StartAsync(default);
        service.StartHosting();

        await service.RunCycleAsync(default);

        Assert.Equal(2, _transport.Sent.Count);
        Assert.False(Decode(_transport.Sent[0].Payload).Hosting);

        var announcement = Decode(_transport.Sent[^1].Payload);
        Assert.True(service.IsHosting);
        Assert.True(announcement.Hosting);
        Assert.Equal(_now, announcement.StartedAt);
    }

    [Fact]
    public async Task StartHosting_announces_the_start_time_of_a_clock_that_is_not_UTC()
    {
        // The service holds the start time as a number of ticks. The window
        // gives the service a local clock. So the service must hold the ticks
        // of the UTC time, and not the ticks of the local time.
        _now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.FromHours(-6));
        await using var service = Build();
        await service.StartAsync(default);
        service.StartHosting();

        await service.RunCycleAsync(default);

        var announcement = Decode(_transport.Sent[^1].Payload);
        Assert.True(announcement.Hosting);
        Assert.Equal(_now, announcement.StartedAt);
        Assert.Equal(_now.UtcDateTime, announcement.StartedAt!.Value.UtcDateTime);
    }

    [Fact]
    public async Task StopHosting_clears_the_host_flag()
    {
        await using var service = Build();
        await service.StartAsync(default);
        service.StartHosting();
        service.StopHosting();

        await service.RunCycleAsync(default);

        Assert.Equal(2, _transport.Sent.Count);
        Assert.False(service.IsHosting);

        var announcement = Decode(_transport.Sent[^1].Payload);
        Assert.False(announcement.Hosting);
        Assert.Null(announcement.StartedAt);
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

        // The test also delivers one datagram from inside the subnet. So the
        // result proves that a delivered datagram does arrive.
        _transport.Deliver(
            new Announcement
            {
                NodeId = "bb11223344", Player = "chris", Address = "10.147.17.9",
            }.ToBytes(),
            IPAddress.Parse("10.147.17.9"));
        _transport.Deliver(
            new Announcement
            {
                NodeId = "cc55667788", Player = "sam", Address = "192.168.1.9",
            }.ToBytes(),
            IPAddress.Parse("192.168.1.9"));

        await service.RunCycleAsync(default);

        var peer = Assert.Single(service.Peers);
        Assert.Equal("bb11223344", peer.NodeId);
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
        await service.StartAsync(default);

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
    public async Task RunCycleAsync_absorbs_a_failed_name_save_and_saves_on_the_next_cycle()
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

        // A folder at the path of the file makes the save fail on every
        // platform. The save also fails for a user with every right.
        var configPath = Path.Combine(_folder, "config.json");
        File.Delete(configPath);
        Directory.CreateDirectory(configPath);
        try
        {
            await service.RunCycleAsync(default);
        }
        finally
        {
            Directory.Delete(configPath);
        }

        Assert.NotNull(seen);
        Assert.Equal("chris", Assert.Single(seen).DisplayName);
        Assert.Equal("chris", _config.RememberedNames["bb11223344"]);
        Assert.DoesNotContain("bb11223344", Store.Load().RememberedNames.Keys);

        await service.RunCycleAsync(default);

        Assert.Equal("chris", Store.Load().RememberedNames["bb11223344"]);
    }

    [Fact]
    public async Task RunCycleAsync_does_not_start_a_second_cycle_while_one_is_running()
    {
        var client = new BlockingZeroTierClient();
        await using var service = Build(client);

        // StartAsync starts the transport. It then runs the first cycle. That
        // cycle waits inside the client. So this test awaits StartAsync only
        // after it opens the client.
        var first = service.StartAsync(default);
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
        await using var service = Build(client, timeProvider: time);

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
        await using var service = Build(client, timeProvider: time);
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
        await using var service = Build(client, timeProvider: time);
        await service.StartAsync(default);

        time.Advance(LobbyService.CyclePeriod);
        await client.WhenCalled(2).WaitAsync(WaitLimit);
        time.Advance(LobbyService.CyclePeriod);

        await client.WhenCalled(3).WaitAsync(WaitLimit);
    }

    [Fact]
    public async Task LastCycleFault_holds_the_exception_from_a_failed_cycle()
    {
        var client = new ScriptedZeroTierClient
        {
            FailOn = call => call >= 2 ? new IOException("disk") : null,
        };
        var time = new FakeTimeProvider();
        await using var service = Build(client, timeProvider: time);
        await service.StartAsync(default);

        Assert.Null(service.LastCycleFault);

        time.Advance(LobbyService.CyclePeriod);
        await client.WhenCalled(2).WaitAsync(WaitLimit);
        time.Advance(LobbyService.CyclePeriod);

        // Cycle 3 starts only after the pump catches the exception from cycle
        // 2. Cycle 3 fails as well. So the property holds an exception at this
        // point, whatever the order of the two tasks.
        await client.WhenCalled(3).WaitAsync(WaitLimit);

        var fault = Assert.IsType<IOException>(service.LastCycleFault);
        Assert.Equal("disk", fault.Message);
    }

    [Fact]
    public async Task LastCycleFault_returns_to_null_after_a_cycle_that_completes()
    {
        var client = new ScriptedZeroTierClient
        {
            FailOn = call => call == 2 ? new IOException("disk") : null,
        };
        var time = new FakeTimeProvider();
        await using var service = Build(client, timeProvider: time);
        await service.StartAsync(default);

        time.Advance(LobbyService.CyclePeriod);
        await client.WhenCalled(2).WaitAsync(WaitLimit);
        time.Advance(LobbyService.CyclePeriod);
        await client.WhenCalled(3).WaitAsync(WaitLimit);
        time.Advance(LobbyService.CyclePeriod);

        // Cycle 4 starts only after cycle 3 completes, and cycle 3 is the
        // cycle that clears the property.
        await client.WhenCalled(4).WaitAsync(WaitLimit);

        Assert.Null(service.LastCycleFault);
    }

    [Fact]
    public async Task CurrentHost_never_reports_this_machine()
    {
        await using var service = Build();
        await service.StartAsync(default);
        service.StartHosting();
        await service.RunCycleAsync(default);

        // A directed broadcast reaches this app as well. So the app receives
        // its own announcement.
        _transport.Deliver(_transport.Sent[^1].Payload, IPAddress.Parse("10.147.17.42"));

        await service.RunCycleAsync(default);

        Assert.True(service.IsHosting);
        Assert.Null(service.CurrentHost);
        Assert.Empty(service.Peers);
    }

    [Fact]
    public async Task RunCycleAsync_cuts_a_display_name_that_this_app_would_reject()
    {
        await using var service = Build();
        _config.DisplayName = new string('x', 41);

        await service.StartAsync(default);

        var sent = Assert.Single(_transport.Sent);
        var table = new PeerTable(LobbyService.PeerExpiry, () => _now);
        var allowed = IPv4Math.Subnet(IPAddress.Parse("10.147.17.42"), 24);

        Assert.True(table.Accept(sent.Payload, IPAddress.Parse("10.147.17.42"), allowed));
        Assert.Equal(
            new string('x', PeerTable.MaxPlayerNameLength), Decode(sent.Payload).Player);
    }

    [Fact]
    public async Task RunCycleAsync_keeps_a_value_that_the_caller_wrote_to_the_configuration()
    {
        await using var service = Build();
        await service.StartAsync(default);

        // Task 9 writes the game path to this object after the build. The
        // service must save that value, and not an earlier copy of the file.
        _config.GamePath = @"C:\Games\midtown2.exe";
        _transport.Deliver(
            new Announcement
            {
                NodeId = "bb11223344", Player = "chris", Address = "10.147.17.9",
            }.ToBytes(),
            IPAddress.Parse("10.147.17.9"));

        await service.RunCycleAsync(default);

        var saved = Store.Load();
        Assert.Equal(@"C:\Games\midtown2.exe", saved.GamePath);
        Assert.Equal("chris", saved.RememberedNames["bb11223344"]);
    }

    [Fact]
    public async Task RunCycleAsync_asks_ZeroTier_for_the_network_in_the_configuration()
    {
        await using var service = Build();

        await service.StartAsync(default);

        Assert.Equal("0123456789abcdef", _zeroTier.RequestedNetworkId);
        Assert.Equal("10.147.17.42", service.MyAddress);
    }

    [Fact]
    public async Task RunCycleAsync_remembers_no_name_while_the_ZeroTier_service_is_absent()
    {
        await using var service = Build();
        await service.StartAsync(default);
        _transport.Deliver(
            new Announcement
            {
                NodeId = "bb11223344", Player = "chris", Address = "10.147.17.9",
            }.ToBytes(),
            IPAddress.Parse("10.147.17.9"));
        _zeroTier.Status = null;

        await service.RunCycleAsync(default);

        Assert.Empty(Store.Load().RememberedNames);
        Assert.Empty(_config.RememberedNames);
    }
}
