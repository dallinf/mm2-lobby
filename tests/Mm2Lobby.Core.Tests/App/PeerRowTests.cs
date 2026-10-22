using System.Net;
using Mm2Lobby.App;
using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Tests.App;

public class PeerRowTests
{
    private const string MyVersion = "1.0.0.0";

    private static Peer Build(
        bool isOnline = true,
        bool appRunning = true,
        bool hosting = false,
        string appVersion = MyVersion,
        string? address = "10.147.17.42",
        LinkQuality link = LinkQuality.Direct)
        => new(
            "a09acf0233",
            "chris",
            appVersion,
            address is null ? null : IPAddress.Parse(address),
            isOnline,
            appRunning,
            hosting,
            null,
            link);

    [Fact]
    public void From_shows_the_name_and_the_address()
    {
        var row = PeerRow.From(Build(), MyVersion);

        Assert.Equal("chris", row.Name);
        Assert.Equal("10.147.17.42", row.Address);
    }

    [Fact]
    public void From_shows_a_dash_for_a_peer_with_no_address()
    {
        var row = PeerRow.From(Build(address: null), MyVersion);

        Assert.Equal("—", row.Address);
    }

    [Fact]
    public void From_shows_that_the_peer_hosts_a_race()
    {
        var row = PeerRow.From(Build(hosting: true), MyVersion);

        Assert.Equal("Hosts a race", row.State);
    }

    [Fact]
    public void From_shows_that_the_peer_is_in_the_lobby()
    {
        var row = PeerRow.From(Build(appRunning: true), MyVersion);

        Assert.Equal("In the lobby", row.State);
    }

    [Fact]
    public void From_tells_an_online_peer_with_no_app_apart_from_an_offline_peer()
    {
        var online = PeerRow.From(Build(appRunning: false), MyVersion);
        var offline = PeerRow.From(Build(isOnline: false, appRunning: false), MyVersion);

        Assert.Equal("Online, the app is not running", online.State);
        Assert.Equal("Offline", offline.State);
        Assert.NotEqual(online.State, offline.State);
    }

    [Theory]
    [InlineData(LinkQuality.Direct, "Direct")]
    [InlineData(LinkQuality.Relayed, "Relayed")]
    [InlineData(LinkQuality.Unknown, "—")]
    public void From_shows_the_link_quality(LinkQuality link, string expected)
    {
        var row = PeerRow.From(Build(link: link), MyVersion);

        Assert.Equal(expected, row.Link);
    }

    [Fact]
    public void From_marks_a_peer_that_runs_another_version()
    {
        var row = PeerRow.From(Build(appVersion: "0.9.0.0"), MyVersion);

        Assert.True(row.VersionDiffers);
        Assert.Contains("0.9.0.0", row.VersionNote);
    }

    [Fact]
    public void From_marks_no_warning_for_the_same_version()
    {
        var row = PeerRow.From(Build(appVersion: MyVersion), MyVersion);

        Assert.False(row.VersionDiffers);
        Assert.Equal(string.Empty, row.VersionNote);
    }

    [Fact]
    public void From_marks_no_warning_for_a_peer_that_sent_no_announcement()
    {
        // An empty version means that the app of the peer is not running. No
        // version exists to compare, so the window must show no warning.
        var row = PeerRow.From(Build(appVersion: string.Empty, appRunning: false), MyVersion);

        Assert.False(row.VersionDiffers);
        Assert.Equal(string.Empty, row.VersionNote);
    }
}
