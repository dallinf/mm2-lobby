using System.Net;
using Mm2Lobby.Core.Processes;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Tests.ZeroTier;

public class ZeroTierClientTests
{
    private const string NetworkId = "0123456789abcdef";

    private static ZeroTierClient Build(FakeProcessRunner runner) => new(runner);

    [Fact]
    public async Task GetStatusAsync_reads_the_node_id_and_the_online_flag()
    {
        var runner = new FakeProcessRunner()
            .ReturnsJson("zerotier-cli", "-j info", ZeroTierFixtures.Info);

        var status = await Build(runner).GetStatusAsync(default);

        Assert.NotNull(status);
        Assert.Equal("a09acf0233", status.NodeId);
        Assert.True(status.ServiceOnline);
    }

    [Fact]
    public async Task GetStatusAsync_reports_the_service_offline()
    {
        var runner = new FakeProcessRunner()
            .ReturnsJson("zerotier-cli", "-j info", ZeroTierFixtures.InfoOffline);

        var status = await Build(runner).GetStatusAsync(default);

        Assert.NotNull(status);
        Assert.False(status.ServiceOnline);
    }

    [Fact]
    public async Task GetStatusAsync_returns_null_when_the_command_is_missing()
    {
        var status = await Build(new FakeProcessRunner()).GetStatusAsync(default);
        Assert.Null(status);
    }

    [Fact]
    public async Task GetStatusAsync_returns_null_for_malformed_output()
    {
        var runner = new FakeProcessRunner()
            .ReturnsJson("zerotier-cli", "-j info", "not json at all");

        Assert.Null(await Build(runner).GetStatusAsync(default));
    }

    [Fact]
    public async Task GetNetworkAsync_reads_the_assigned_address_and_the_prefix()
    {
        var runner = new FakeProcessRunner()
            .ReturnsJson("zerotier-cli", "-j listnetworks", ZeroTierFixtures.ListNetworks);

        var network = await Build(runner).GetNetworkAsync(NetworkId, default);

        Assert.NotNull(network);
        Assert.Equal(IPAddress.Parse("10.147.17.42"), network.Address);
        Assert.Equal(24, network.PrefixLength);
        Assert.True(network.IsJoined);
    }

    [Fact]
    public async Task GetNetworkAsync_skips_an_IPv6_address()
    {
        var runner = new FakeProcessRunner()
            .ReturnsJson("zerotier-cli", "-j listnetworks", ZeroTierFixtures.ListNetworksIPv6First);

        var network = await Build(runner).GetNetworkAsync(NetworkId, default);

        Assert.Equal(IPAddress.Parse("10.147.17.42"), network!.Address);
        Assert.Equal(24, network.PrefixLength);
    }

    [Fact]
    public async Task GetNetworkAsync_reports_a_network_it_has_not_joined()
    {
        var runner = new FakeProcessRunner()
            .ReturnsJson("zerotier-cli", "-j listnetworks", ZeroTierFixtures.ListNetworksNotJoined);

        var network = await Build(runner).GetNetworkAsync(NetworkId, default);

        Assert.NotNull(network);
        Assert.Null(network.Address);
        Assert.False(network.IsJoined);
        Assert.Equal("REQUESTING_CONFIGURATION", network.Status);
    }

    [Fact]
    public async Task GetNetworkAsync_returns_null_for_an_unknown_network()
    {
        var runner = new FakeProcessRunner()
            .ReturnsJson("zerotier-cli", "-j listnetworks", ZeroTierFixtures.ListNetworks);

        Assert.Null(await Build(runner).GetNetworkAsync("ffffffffffffffff", default));
    }

    [Fact]
    public async Task GetLinkQualityAsync_classifies_each_leaf_and_drops_the_roots()
    {
        var runner = new FakeProcessRunner()
            .ReturnsJson("zerotier-cli", "-j listpeers", ZeroTierFixtures.ListPeers);

        var quality = await Build(runner).GetLinkQualityAsync(default);

        Assert.Equal(3, quality.Count);
        Assert.Equal(LinkQuality.Direct, quality["bb11223344"]);
        Assert.Equal(LinkQuality.Relayed, quality["cc55667788"]);
        Assert.Equal(LinkQuality.Relayed, quality["dd99887766"]);
        Assert.False(quality.ContainsKey("778cde7190"));
    }

    [Fact]
    public async Task GetLinkQualityAsync_is_case_insensitive()
    {
        var runner = new FakeProcessRunner()
            .ReturnsJson("zerotier-cli", "-j listpeers", ZeroTierFixtures.ListPeers);

        var quality = await Build(runner).GetLinkQualityAsync(default);

        Assert.Equal(LinkQuality.Direct, quality["BB11223344"]);
    }

    [Fact]
    public async Task GetLinkQualityAsync_returns_an_empty_map_for_malformed_output()
    {
        var runner = new FakeProcessRunner()
            .ReturnsJson("zerotier-cli", "-j listpeers", "{ not an array }");

        Assert.Empty(await Build(runner).GetLinkQualityAsync(default));
    }
}
