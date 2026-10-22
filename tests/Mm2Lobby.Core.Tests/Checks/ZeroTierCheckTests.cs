using System.Net;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Core.ZeroTier;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

public class ZeroTierCheckTests
{
    private const string NetworkId = "0123456789abcdef";

    private readonly FakeZeroTierClient _zeroTier = new();
    private readonly AppConfig _config = new() { ZeroTierNetworkId = NetworkId };

    private ZeroTierCheck Check() => new(_zeroTier, _config);

    [Fact]
    public async Task Passes_when_the_service_is_online_and_the_network_is_joined()
    {
        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains("10.147.17.42", result.Message);
        Assert.Null(result.FixDescription);
        Assert.Equal(NetworkId, _zeroTier.RequestedNetworkId);
    }

    [Fact]
    public async Task Fails_with_the_download_page_when_the_app_cannot_reach_ZeroTier()
    {
        _zeroTier.Status = null;

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("https://www.zerotier.com/download/", result.FixDescription);
        Assert.Contains("Open Windows Services", result.FixDescription);
        Assert.Contains("ZeroTier One", result.FixDescription);
        Assert.Contains("authentication token", result.Message);
        Assert.Contains("run it as an administrator", result.FixDescription);
        Assert.Null(_zeroTier.RequestedNetworkId);
    }

    [Fact]
    public async Task Fails_with_an_internet_hint_when_the_service_runs_but_is_offline()
    {
        _zeroTier.Status = new ZeroTierStatus(false, "a09acf0233");

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("internet", result.FixDescription);
        Assert.DoesNotContain("download", result.FixDescription);
        Assert.Null(_zeroTier.RequestedNetworkId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Fails_without_asking_ZeroTier_when_no_network_ID_is_set(string networkId)
    {
        _config.ZeroTierNetworkId = networkId;

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Null(_zeroTier.RequestedNetworkId);
        Assert.Contains("network ID", result.Message);
    }

    [Fact]
    public async Task Fails_with_the_network_ID_to_join_when_the_machine_has_not_joined()
    {
        _zeroTier.Network = null;

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains(NetworkId, result.Message);
        Assert.Contains(NetworkId, result.FixDescription);
        Assert.Contains("Join Network", result.FixDescription);
    }

    [Theory]
    [InlineData("ACCESS_DENIED", "approve")]
    [InlineData("NOT_FOUND", "network ID")]
    [InlineData("REQUESTING_CONFIGURATION", "wait")]
    public async Task Names_the_cause_for_each_status_that_has_no_address(
        string status, string expectedInFix)
    {
        _zeroTier.Network = new ZeroTierNetwork(NetworkId, status, null, 0);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains(status, result.Message);
        Assert.Contains(expectedInFix, result.FixDescription);
    }

    [Fact]
    public async Task Tells_the_player_to_update_ZeroTier_when_the_status_is_CLIENT_TOO_OLD()
    {
        _zeroTier.Network = new ZeroTierNetwork(NetworkId, "CLIENT_TOO_OLD", null, 0);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("CLIENT_TOO_OLD", result.Message);
        Assert.Contains("Update ZeroTier", result.FixDescription);
        Assert.Contains("https://www.zerotier.com/download/", result.FixDescription);
        Assert.DoesNotContain("Wait", result.FixDescription);
    }

    [Fact]
    public async Task Fails_when_the_status_is_OK_but_no_address_is_assigned()
    {
        _zeroTier.Network = new ZeroTierNetwork(NetworkId, "OK", null, 0);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("address", result.Message);
    }

    [Fact]
    public async Task Fails_when_an_address_exists_but_the_status_is_not_OK()
    {
        _zeroTier.Network = new ZeroTierNetwork(
            NetworkId, "ACCESS_DENIED", IPAddress.Parse("10.147.17.42"), 24);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
    }

    [Fact]
    public async Task Reads_the_network_ID_from_the_shared_configuration_on_every_run()
    {
        var check = Check();
        _config.ZeroTierNetworkId = "fedcba9876543210";
        _zeroTier.Network = new ZeroTierNetwork(
            "fedcba9876543210", "OK", IPAddress.Parse("10.147.17.42"), 24);

        var result = await check.RunAsync(default);

        Assert.Equal("fedcba9876543210", _zeroTier.RequestedNetworkId);
        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Fact]
    public async Task FixAsync_checks_again_and_reports_the_new_state()
    {
        var check = Check();
        _zeroTier.Status = null;
        Assert.Equal(CheckStatus.Fail, (await check.RunAsync(default)).Status);

        _zeroTier.Status = new ZeroTierStatus(true, "a09acf0233");
        var fixedResult = await check.FixAsync(default);

        Assert.Equal(CheckStatus.Pass, fixedResult.Status);
    }

    [Fact]
    public async Task FixAsync_still_fails_when_the_cause_is_still_present()
    {
        _zeroTier.Network = null;

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
    }

    [Fact]
    public void Has_the_name_ZeroTier()
    {
        Assert.Equal("ZeroTier", Check().Name);
    }
}
