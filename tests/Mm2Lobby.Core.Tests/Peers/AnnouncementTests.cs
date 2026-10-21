using System.Text;
using Mm2Lobby.Core.Peers;

namespace Mm2Lobby.Core.Tests.Peers;

public class AnnouncementTests
{
    [Fact]
    public void ToBytes_then_TryParse_round_trips_every_field()
    {
        var original = new Announcement
        {
            AppVersion = "1.0.0",
            NodeId = "a09acf0233",
            Player = "dallin",
            Address = "10.147.17.42",
            Hosting = true,
            StartedAt = new DateTimeOffset(2026, 10, 21, 18, 4, 11, TimeSpan.Zero),
        };

        var parsed = Announcement.TryParse(original.ToBytes());

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void ToBytes_writes_camel_case_names()
    {
        var json = Encoding.UTF8.GetString(
            new Announcement { NodeId = "a09acf0233", Player = "dallin" }.ToBytes());

        Assert.Contains("\"nodeId\"", json);
        Assert.Contains("\"appVersion\"", json);
        Assert.DoesNotContain("\"NodeId\"", json);
    }

    [Fact]
    public void Proto_defaults_to_the_current_version()
    {
        Assert.Equal(1, Announcement.CurrentProto);
        Assert.Equal(Announcement.CurrentProto, new Announcement().Proto);
    }

    [Fact]
    public void TryParse_returns_null_for_bytes_that_are_not_json()
    {
        Assert.Null(Announcement.TryParse(Encoding.UTF8.GetBytes("<html>nope</html>")));
    }

    [Fact]
    public void TryParse_returns_null_for_json_of_the_wrong_shape()
    {
        Assert.Null(Announcement.TryParse(Encoding.UTF8.GetBytes("[1, 2, 3]")));
    }

    [Fact]
    public void TryParse_fills_the_defaults_for_absent_fields()
    {
        var parsed = Announcement.TryParse(
            Encoding.UTF8.GetBytes("""{"proto":1,"nodeId":"a09acf0233"}"""));

        Assert.NotNull(parsed);
        Assert.Equal("a09acf0233", parsed.NodeId);
        Assert.Equal(string.Empty, parsed.Player);
        Assert.False(parsed.Hosting);
        Assert.Null(parsed.StartedAt);
    }
}
