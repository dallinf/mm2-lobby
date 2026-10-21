using Mm2Lobby.Core.Lobby;
using Mm2Lobby.Core.Peers;

namespace Mm2Lobby.Core.Tests;

public class SpecConstantsTests
{
    // Every other test reads the constant that it checks, so a change to one
    // of these values passes the whole suite. This test holds the literal
    // values from the specification. Each one is part of the protocol, and a
    // change to one of them stops this app from finding a peer that runs an
    // earlier copy.
    [Fact]
    public void The_specification_fixes_the_protocol_values()
    {
        Assert.Equal(1, Announcement.CurrentProto);
        Assert.Equal(48200, LobbyService.Port);
        Assert.Equal(TimeSpan.FromSeconds(2), LobbyService.CyclePeriod);
        Assert.Equal(TimeSpan.FromSeconds(6), LobbyService.PeerExpiry);
        Assert.Equal(32, PeerTable.MaxPlayerNameLength);
    }
}
