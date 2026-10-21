using System.Net;
using System.Text;
using Mm2Lobby.Core.Peers;

namespace Mm2Lobby.Core.Tests.Peers;

public class PeerTableTests
{
    private static readonly IPNetwork Allowed =
        IPv4Math.Subnet(IPAddress.Parse("10.147.17.42"), 24);

    private static readonly IPAddress Source = IPAddress.Parse("10.147.17.9");

    private DateTimeOffset _now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);

    private PeerTable Build() => new(TimeSpan.FromSeconds(6), () => _now);

    private static byte[] Good(Action<AnnouncementBuilder>? tweak = null)
    {
        var builder = new AnnouncementBuilder();
        tweak?.Invoke(builder);
        return builder.Build().ToBytes();
    }

    private sealed class AnnouncementBuilder
    {
        public int Proto { get; set; } = Announcement.CurrentProto;
        public string NodeId { get; set; } = "bb11223344";
        public string Player { get; set; } = "chris";
        public string Address { get; set; } = "10.147.17.9";
        public bool Hosting { get; set; }

        public Announcement Build() => new()
        {
            Proto = Proto,
            AppVersion = "1.0.0",
            NodeId = NodeId,
            Player = Player,
            Address = Address,
            Hosting = Hosting,
        };
    }

    [Fact]
    public void Accept_records_a_valid_announcement()
    {
        var table = Build();

        Assert.True(table.Accept(Good(), Source, Allowed));

        var peer = Assert.Single(table.GetLive());
        Assert.Equal("bb11223344", peer.NodeId);
        Assert.Equal("chris", peer.Player);
        Assert.Equal(Source, peer.Address);
        Assert.Equal(_now, peer.HeardAt);
    }

    [Fact]
    public void Accept_rejects_a_source_outside_the_allowed_subnet()
    {
        var table = Build();
        var stranger = IPAddress.Parse("192.168.1.50");

        Assert.False(table.Accept(
            Good(a => a.Address = "192.168.1.50"), stranger, Allowed));
        Assert.Empty(table.GetLive());
    }

    [Fact]
    public void Accept_rejects_bytes_that_are_not_an_announcement()
    {
        var table = Build();

        Assert.False(table.Accept(Encoding.UTF8.GetBytes("hello"), Source, Allowed));
        Assert.Empty(table.GetLive());
    }

    [Fact]
    public void Accept_rejects_an_unknown_protocol_version()
    {
        var table = Build();

        Assert.False(table.Accept(Good(a => a.Proto = 2), Source, Allowed));
        Assert.Empty(table.GetLive());
    }

    [Fact]
    public void Accept_rejects_an_empty_node_id()
    {
        var table = Build();

        Assert.False(table.Accept(Good(a => a.NodeId = ""), Source, Allowed));
    }

    [Fact]
    public void Accept_rejects_an_empty_player_name()
    {
        var table = Build();

        Assert.False(table.Accept(Good(a => a.Player = "   "), Source, Allowed));
    }

    [Fact]
    public void Accept_rejects_an_over_long_player_name()
    {
        var table = Build();
        var tooLong = new string('x', PeerTable.MaxPlayerNameLength + 1);

        Assert.False(table.Accept(Good(a => a.Player = tooLong), Source, Allowed));
    }

    [Fact]
    public void Accept_accepts_a_player_name_at_the_maximum_length()
    {
        var table = Build();
        var longest = new string('x', PeerTable.MaxPlayerNameLength);

        Assert.True(table.Accept(Good(a => a.Player = longest), Source, Allowed));
        Assert.Single(table.GetLive());
    }

    [Fact]
    public void Accept_rejects_a_payload_that_claims_another_address()
    {
        var table = Build();

        Assert.False(table.Accept(Good(a => a.Address = "10.147.17.200"), Source, Allowed));
    }

    [Fact]
    public void Accept_rejects_an_address_that_does_not_parse()
    {
        var table = Build();

        Assert.False(table.Accept(Good(a => a.Address = "not-an-address"), Source, Allowed));
        Assert.Empty(table.GetLive());
    }

    [Fact]
    public void Accept_replaces_an_earlier_announcement_from_the_same_node()
    {
        var table = Build();
        table.Accept(Good(), Source, Allowed);

        _now = _now.AddSeconds(2);
        table.Accept(Good(a => a.Hosting = true), Source, Allowed);

        var peer = Assert.Single(table.GetLive());
        Assert.True(peer.Hosting);
        Assert.Equal(_now, peer.HeardAt);
    }

    [Fact]
    public void GetLive_keeps_a_peer_heard_inside_the_expiry()
    {
        var table = Build();
        table.Accept(Good(), Source, Allowed);

        _now = _now.AddSeconds(5);

        Assert.Single(table.GetLive());
    }

    [Fact]
    public void GetLive_drops_a_peer_heard_outside_the_expiry()
    {
        var table = Build();
        Assert.True(table.Accept(Good(), Source, Allowed));
        Assert.Single(table.GetLive());

        _now = _now.AddSeconds(7);

        Assert.Empty(table.GetLive());
    }

    [Fact]
    public void GetLive_keeps_a_peer_heard_just_inside_the_expiry()
    {
        var table = Build();
        Assert.True(table.Accept(Good(), Source, Allowed));
        Assert.Single(table.GetLive());

        _now = _now.AddMilliseconds(5999);

        Assert.Single(table.GetLive());
    }

    [Fact]
    public void GetLive_drops_a_peer_heard_exactly_at_the_expiry()
    {
        var table = Build();
        Assert.True(table.Accept(Good(), Source, Allowed));
        Assert.Single(table.GetLive());

        _now = _now.AddSeconds(6);

        Assert.Empty(table.GetLive());
    }

    [Fact]
    public void GetLive_lower_cases_the_node_id()
    {
        var table = Build();
        table.Accept(Good(a => a.NodeId = "BB11223344"), Source, Allowed);

        Assert.Equal("bb11223344", Assert.Single(table.GetLive()).NodeId);
    }
}
