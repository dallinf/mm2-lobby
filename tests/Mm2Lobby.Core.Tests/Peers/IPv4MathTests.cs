using System.Net;
using Mm2Lobby.Core.Peers;

namespace Mm2Lobby.Core.Tests.Peers;

public class IPv4MathTests
{
    [Theory]
    [InlineData("10.147.17.42", 24, "10.147.17.0")]
    [InlineData("10.147.17.42", 16, "10.147.0.0")]
    [InlineData("192.168.1.130", 25, "192.168.1.128")]
    public void Subnet_masks_off_the_host_bits(string address, int prefix, string expected)
    {
        var subnet = IPv4Math.Subnet(IPAddress.Parse(address), prefix);

        Assert.Equal(IPAddress.Parse(expected), subnet.BaseAddress);
        Assert.Equal(prefix, subnet.PrefixLength);
    }

    [Theory]
    [InlineData("10.147.17.42", 24, "10.147.17.255")]
    [InlineData("10.147.17.42", 16, "10.147.255.255")]
    [InlineData("192.168.1.130", 25, "192.168.1.255")]
    public void BroadcastAddress_sets_every_host_bit(string address, int prefix, string expected)
    {
        var broadcast = IPv4Math.BroadcastAddress(IPAddress.Parse(address), prefix);

        Assert.Equal(IPAddress.Parse(expected), broadcast);
    }

    [Fact]
    public void Subnet_contains_another_address_in_the_same_range()
    {
        var subnet = IPv4Math.Subnet(IPAddress.Parse("10.147.17.42"), 24);

        Assert.True(subnet.Contains(IPAddress.Parse("10.147.17.9")));
        Assert.False(subnet.Contains(IPAddress.Parse("192.168.1.9")));
    }

    [Fact]
    public void Subnet_rejects_an_IPv6_address()
    {
        Assert.Throws<ArgumentException>(
            () => IPv4Math.Subnet(IPAddress.Parse("fd01:2345::1"), 88));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void Subnet_rejects_an_impossible_prefix(int prefix)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IPv4Math.Subnet(IPAddress.Parse("10.0.0.1"), prefix));
    }
}
