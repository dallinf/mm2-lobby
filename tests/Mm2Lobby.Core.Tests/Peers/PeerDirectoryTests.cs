using System.Net;
using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Tests.Peers;

public class PeerDirectoryTests
{
    private const string MyNodeId = "a09acf0233";

    private static readonly IPNetwork Allowed =
        IPv4Math.Subnet(IPAddress.Parse("10.147.17.42"), 24);

    private readonly DateTimeOffset _now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);
    private readonly FakeZeroTierClient _zeroTier = new();
    private readonly PeerTable _table;

    public PeerDirectoryTests()
        => _table = new PeerTable(TimeSpan.FromSeconds(6), () => _now);

    private PeerDirectory Build() => new(_zeroTier, _table);

    private static readonly Dictionary<string, string> NoNames =
        new(StringComparer.OrdinalIgnoreCase);

    private void Hear(string nodeId, string player, string address, bool hosting = false)
    {
        var payload = new Announcement
        {
            AppVersion = "1.0.0",
            NodeId = nodeId,
            Player = player,
            Address = address,
            Hosting = hosting,
            StartedAt = hosting ? _now : null,
        }.ToBytes();

        Assert.True(_table.Accept(payload, IPAddress.Parse(address), Allowed));
    }

    [Fact]
    public async Task A_peer_we_hear_from_is_online_with_its_app_running()
    {
        Hear("bb11223344", "chris", "10.147.17.9");
        _zeroTier.Quality["bb11223344"] = LinkQuality.Direct;

        var peers = await Build().GetPeersAsync(MyNodeId, NoNames, default);

        var peer = Assert.Single(peers);
        Assert.Equal("chris", peer.DisplayName);
        Assert.Equal(IPAddress.Parse("10.147.17.9"), peer.Address);
        Assert.True(peer.IsOnline);
        Assert.True(peer.AppRunning);
        Assert.Equal(LinkQuality.Direct, peer.Link);
    }

    [Fact]
    public async Task A_node_in_listpeers_that_we_never_hear_is_online_without_its_app()
    {
        _zeroTier.Quality["cc55667788"] = LinkQuality.Relayed;
        var remembered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cc55667788"] = "sam",
        };

        var peers = await Build().GetPeersAsync(MyNodeId, remembered, default);

        var peer = Assert.Single(peers);
        Assert.Equal("sam", peer.DisplayName);
        Assert.Null(peer.Address);
        Assert.True(peer.IsOnline);
        Assert.False(peer.AppRunning);
        Assert.Equal(LinkQuality.Relayed, peer.Link);
    }

    [Fact]
    public async Task A_remembered_node_that_appears_nowhere_is_offline()
    {
        var remembered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dd99887766"] = "jo",
        };

        var peers = await Build().GetPeersAsync(MyNodeId, remembered, default);

        var peer = Assert.Single(peers);
        Assert.Equal("jo", peer.DisplayName);
        Assert.False(peer.IsOnline);
        Assert.False(peer.AppRunning);
        Assert.Equal(LinkQuality.Unknown, peer.Link);
    }

    [Fact]
    public async Task A_node_we_never_heard_and_never_remembered_shows_its_node_id()
    {
        _zeroTier.Quality["cc55667788"] = LinkQuality.Relayed;

        var peers = await Build().GetPeersAsync(MyNodeId, NoNames, default);

        Assert.Equal("cc55667788", Assert.Single(peers).DisplayName);
    }

    [Fact]
    public async Task This_machine_never_appears_in_its_own_list()
    {
        Hear(MyNodeId, "dallin", "10.147.17.42");
        _zeroTier.Quality[MyNodeId] = LinkQuality.Direct;
        var remembered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MyNodeId] = "dallin",
        };

        Assert.Empty(await Build().GetPeersAsync(MyNodeId, remembered, default));
    }

    [Fact]
    public async Task This_machine_is_excluded_when_the_node_id_differs_only_in_case()
    {
        Hear(MyNodeId, "dallin", "10.147.17.42");
        _zeroTier.Quality[MyNodeId] = LinkQuality.Direct;
        var remembered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MyNodeId] = "dallin",
        };

        var peers = await Build().GetPeersAsync(
            MyNodeId.ToUpperInvariant(), remembered, default);

        Assert.Empty(peers);
    }

    [Fact]
    public async Task A_hosting_peer_carries_its_hosting_flag_and_start_time()
    {
        Hear("ee11112222", "zoe", "10.147.17.11", hosting: true);

        var peers = await Build().GetPeersAsync(MyNodeId, NoNames, default);

        var peer = Assert.Single(peers);
        Assert.True(peer.Hosting);
        Assert.Equal(_now, peer.StartedAt);
    }

    [Fact]
    public async Task A_heard_peer_without_a_link_quality_reports_unknown()
    {
        Hear("bb11223344", "chris", "10.147.17.9");

        var peers = await Build().GetPeersAsync(MyNodeId, NoNames, default);

        Assert.Equal(LinkQuality.Unknown, Assert.Single(peers).Link);
    }

    [Fact]
    public async Task The_host_sorts_first_then_the_running_apps_then_the_rest()
    {
        Hear("bb11223344", "chris", "10.147.17.9");
        Hear("ee11112222", "zoe", "10.147.17.11", hosting: true);
        _zeroTier.Quality["cc55667788"] = LinkQuality.Relayed;
        var remembered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cc55667788"] = "bea",
            ["dd99887766"] = "ann",
        };

        var peers = await Build().GetPeersAsync(MyNodeId, remembered, default);

        Assert.Equal(new[] { "zoe", "chris", "bea", "ann" },
            peers.Select(p => p.DisplayName).ToArray());
    }

    [Fact]
    public async Task Every_peer_carries_the_app_version_that_the_table_holds()
    {
        Hear("bb11223344", "chris", "10.147.17.9");

        // A datagram can hold "appVersion":null. Peer.AppVersion does not
        // accept a null.
        Assert.True(_table.Accept(
            System.Text.Encoding.UTF8.GetBytes("""
            {"proto":1,"appVersion":null,"nodeId":"ee11112222","player":"zoe",
             "address":"10.147.17.11"}
            """),
            IPAddress.Parse("10.147.17.11"),
            Allowed));

        _zeroTier.Quality["cc55667788"] = LinkQuality.Relayed;
        var remembered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dd99887766"] = "jo",
        };

        var peers = await Build().GetPeersAsync(MyNodeId, remembered, default);

        Assert.Equal("1.0.0", peers.Single(p => p.NodeId == "bb11223344").AppVersion);
        Assert.Equal(string.Empty, peers.Single(p => p.NodeId == "ee11112222").AppVersion);
        Assert.Equal(string.Empty, peers.Single(p => p.NodeId == "cc55667788").AppVersion);
        Assert.Equal(string.Empty, peers.Single(p => p.NodeId == "dd99887766").AppVersion);
    }

    [Fact]
    public async Task Two_peers_with_one_name_sort_by_the_node_id()
    {
        var remembered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ff99887766"] = "sam",
            ["aa11223344"] = "sam",
        };

        var peers = await Build().GetPeersAsync(MyNodeId, remembered, default);

        Assert.Equal(
            new[] { "aa11223344", "ff99887766" }, peers.Select(p => p.NodeId).ToArray());
    }

    [Fact]
    public async Task A_peer_heard_outside_the_expiry_falls_back_to_app_not_running()
    {
        var clock = new DateTimeOffset(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);
        var table = new PeerTable(TimeSpan.FromSeconds(6), () => clock);
        var payload = new Announcement
        {
            NodeId = "bb11223344", Player = "chris", Address = "10.147.17.9",
        }.ToBytes();
        Assert.True(table.Accept(payload, IPAddress.Parse("10.147.17.9"), Allowed));

        clock = clock.AddSeconds(7);
        _zeroTier.Quality["bb11223344"] = LinkQuality.Direct;

        var peers = await new PeerDirectory(_zeroTier, table)
            .GetPeersAsync(MyNodeId, NoNames, default);

        var peer = Assert.Single(peers);
        Assert.True(peer.IsOnline);
        Assert.False(peer.AppRunning);
    }
}
