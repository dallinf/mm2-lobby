using Mm2Lobby.Core.Configuration;

namespace Mm2Lobby.Core.Tests.Configuration;

public class NetworkIdRuleTests
{
    [Theory]
    [InlineData("0123456789abcdef", "0123456789abcdef")]
    [InlineData("8056c2e21c000001", "8056c2e21c000001")]
    // A player pastes the ID from ZeroTier Central. That paste can carry a
    // space at each end, and ZeroTier Central shows some IDs in capitals.
    [InlineData("0123456789ABCDEF", "0123456789abcdef")]
    [InlineData("   0123456789abcdef   ", "0123456789abcdef")]
    [InlineData("\t0123456789AbCdEf\n", "0123456789abcdef")]
    public void TryRead_accepts_16_hexadecimal_characters(string typed, string expected)
    {
        Assert.True(NetworkIdRule.TryRead(typed, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789abcdef0")]
    [InlineData("0123456789abcdeg")]
    [InlineData("0123456789abcde ")]
    [InlineData("01234567 9abcdef")]
    [InlineData("0123456789abcde-")]
    [InlineData("my network")]
    public void TryRead_refuses_every_other_value(string? typed)
    {
        Assert.False(NetworkIdRule.TryRead(typed, out var id));
        Assert.Equal(string.Empty, id);
    }

    [Fact]
    public void ZeroTier_gives_every_network_an_ID_of_16_characters()
        => Assert.Equal(16, NetworkIdRule.Length);

    [Fact]
    public void ZeroTier_gives_every_node_an_ID_of_10_characters()
        => Assert.Equal(10, NetworkIdRule.NodeIdLength);

    [Theory]
    [InlineData("88c5b1f33969ae66", "88c5b1f339")]
    [InlineData("0123456789abcdef", "0123456789")]
    // TryRead accepts the two forms that a paste gives, so this method
    // accepts them too, and it answers one form only: lower case.
    [InlineData("88C5B1F33969AE66", "88c5b1f339")]
    [InlineData("   88c5b1f33969ae66   ", "88c5b1f339")]
    public void ControllerNodeId_reads_the_first_10_characters(
        string networkId, string expected)
        => Assert.Equal(expected, NetworkIdRule.ControllerNodeId(networkId));

    // The app holds no network ID before the player sets one, and a player can
    // paste a part of an ID. The empty answer then names no node, and the
    // caller excludes nobody. A short ID must not name the node that it holds:
    // "88c5b1f339" is a node ID, and it belongs to a player as much as to a
    // controller.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("88c5b1f339")]
    [InlineData("88c5b1f33969ae6")]
    [InlineData("88c5b1f33969ae660")]
    [InlineData("88c5b1f33969ae6g")]
    [InlineData("my network")]
    public void ControllerNodeId_answers_the_empty_string_for_every_other_value(
        string? networkId)
        => Assert.Equal(string.Empty, NetworkIdRule.ControllerNodeId(networkId));
}
