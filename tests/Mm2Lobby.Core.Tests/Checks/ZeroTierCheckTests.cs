using System.Net;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Core.ZeroTier;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

public class ZeroTierCheckTests
{
    private const string NetworkId = "0123456789abcdef";

    private const string Command = @"C:\Program Files (x86)\ZeroTier\One\zerotier-cli.bat";

    private static readonly string[] Folders =
    {
        @"C:\Program Files (x86)\ZeroTier\One",
        @"C:\Program Files\ZeroTier\One",
    };

    private readonly FakeZeroTierClient _zeroTier = new();
    private readonly AppConfig _config = new() { ZeroTierNetworkId = NetworkId };
    private readonly FakeProcessRunner _runner = new();

    private string _commandFile = Command;

    public ZeroTierCheckTests()
    {
        // The command runs, and ZeroTier gives no answer. This is the state
        // that every test below shares, apart from the two tests that name
        // another fault.
        _runner.Returns(
            Command, "-j info", new ProcessResult(1, "", "the service is not running"));
    }

    private ZeroTierCheck Check() => new(
        _zeroTier,
        _config,
        new ZeroTierFaultReader(_runner, new ZeroTierCommand(_commandFile, Folders)));

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
    public async Task Fails_with_the_Services_advice_when_ZeroTier_gives_no_answer()
    {
        _zeroTier.Status = null;

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("type Services", result.FixDescription);
        Assert.Contains("ZeroTier One", result.FixDescription);
        Assert.Contains("Status column of ZeroTier One is blank", result.FixDescription);
        Assert.DoesNotContain("Stopped", result.FixDescription);
        Assert.Contains("authentication token", result.Message);
        Assert.Contains("run it as an administrator", result.FixDescription);
        Assert.Null(_zeroTier.RequestedNetworkId);
    }

    /// <summary>
    /// The real Windows run that found this defect: the name zerotier-cli does
    /// not resolve, and the app reported a stopped service. The two faults have
    /// two different fixes, so the text must keep them apart.
    /// </summary>
    [Fact]
    public async Task Says_that_it_did_not_find_ZeroTier_and_names_the_folders()
    {
        _zeroTier.Status = null;

        // The locator found no command file, so it kept the plain name. The
        // runner does not know that name, so it answers NotRunExitCode.
        _commandFile = "zerotier-cli";

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("did not find the ZeroTier command", result.Message);
        Assert.Contains("zerotier-cli.bat", result.FixDescription);
        Assert.Contains(Folders[0], result.FixDescription);
        Assert.Contains(Folders[1], result.FixDescription);
        Assert.Contains("https://www.zerotier.com/download/", result.FixDescription);
        Assert.Null(_zeroTier.RequestedNetworkId);
    }

    [Fact]
    public async Task Does_not_report_a_command_that_it_did_not_find_as_a_stopped_service()
    {
        _zeroTier.Status = null;
        _commandFile = "zerotier-cli";

        var result = await Check().RunAsync(default);

        var text = result.Message + " " + result.FixDescription;
        Assert.DoesNotContain("stopped", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not running", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Services", text);
    }

    [Fact]
    public async Task Names_no_folder_when_the_computer_holds_no_program_folder()
    {
        // macOS and Linux set neither program folder variable. The text must
        // still read as a sentence.
        _zeroTier.Status = null;
        _commandFile = "zerotier-cli";

        var check = new ZeroTierCheck(
            _zeroTier,
            _config,
            new ZeroTierFaultReader(
                _runner, new ZeroTierCommand("zerotier-cli", Array.Empty<string>())));

        var result = await check.RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("did not find the ZeroTier command", result.Message);
        Assert.Contains("found no folder of programs", result.FixDescription);
        Assert.Contains("https://www.zerotier.com/download/", result.FixDescription);
    }

    [Fact]
    public async Task Names_the_token_when_ZeroTier_refuses_the_command()
    {
        _zeroTier.Status = null;
        _runner.Returns(
            Command,
            "-j info",
            new ProcessResult(
                ZeroTierFaultReader.TokenExitCode,
                "authtoken.secret not found or readable in C:\\ProgramData\\ZeroTier\\One "
                    + "(try again as root)",
                ""));

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("authentication token", result.Message);
        Assert.Contains("Run as administrator", result.FixDescription);

        // ZeroTier runs. So the text must not send the player to the download
        // page, and it must not send the player to the Services window.
        Assert.DoesNotContain("https://www.zerotier.com/download/", result.FixDescription);
        Assert.DoesNotContain("Services", result.FixDescription);
    }

    [Fact]
    public async Task Runs_no_command_of_its_own_while_ZeroTier_answers()
    {
        // The fault reader runs the command one more time. It must do that on
        // the failure path only.
        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Empty(_runner.Calls);
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

        // The app has no settings screen. The advice must name the box of the
        // window, and it must name the button that starts the lobby.
        Assert.Contains("ZeroTier network ID", result.FixDescription);
        Assert.Contains("Check again", result.FixDescription);
        Assert.DoesNotContain("settings", result.FixDescription);
    }

    [Fact]
    public async Task The_advice_for_NOT_FOUND_names_the_box_and_ZeroTier_Central()
    {
        _zeroTier.Network = new ZeroTierNetwork(NetworkId, "NOT_FOUND", null, 0);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("ZeroTier network ID", result.FixDescription);
        Assert.Contains("16 characters", result.FixDescription);
        Assert.Contains("https://my.zerotier.com", result.FixDescription);
        Assert.DoesNotContain("settings", result.FixDescription);
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
