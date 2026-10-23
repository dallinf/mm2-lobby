using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Tests.ZeroTier;

/// <summary>
/// One method builds the arguments of every ZeroTier command. A later change
/// adds the -T switch of ZeroTier in that one method.
/// </summary>
public class ZeroTierArgumentsTests
{
    [Theory]
    [InlineData("info", "-j info")]
    [InlineData("listnetworks", "-j listnetworks")]
    [InlineData("listpeers", "-j listpeers")]
    public void For_asks_ZeroTier_for_JSON(string command, string expected)
    {
        Assert.Equal(expected, ZeroTierArguments.For(command));
    }

    [Fact]
    public void The_class_names_the_three_commands_that_the_app_runs()
    {
        Assert.Equal("info", ZeroTierArguments.Info);
        Assert.Equal("listnetworks", ZeroTierArguments.ListNetworks);
        Assert.Equal("listpeers", ZeroTierArguments.ListPeers);
    }
}
