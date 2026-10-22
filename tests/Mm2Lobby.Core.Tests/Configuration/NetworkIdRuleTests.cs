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
}
