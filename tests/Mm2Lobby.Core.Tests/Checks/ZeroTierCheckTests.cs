using System.Net;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Core.ZeroTier;
using Mm2Lobby.Windows;
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

    private const string Sid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    private const string SharedData = @"C:\ProgramData";

    /// <summary>The text that zerotier-cli prints when it cannot read the token.</summary>
    private const string TokenText =
        "authtoken.secret not found or readable in C:\\ProgramData\\ZeroTier\\One "
        + "(try again as root)";

    private static readonly string TokenPath =
        Path.Combine(SharedData, "ZeroTier", "One", "authtoken.secret");

    private readonly FakeZeroTierClient _zeroTier = new();
    private readonly AppConfig _config = new() { ZeroTierNetworkId = NetworkId };
    private readonly FakeProcessRunner _runner = new();
    private readonly FakeElevatedProcessRunner _elevated = new();

    private string _commandFile = Command;
    private string? _accountId = Sid;
    private IProcessRunner? _grantRunner;

    public ZeroTierCheckTests()
    {
        // The command runs, and ZeroTier gives no answer. This is the state
        // that every test below shares, apart from the tests that name
        // another fault.
        _runner.Returns(
            Command, "-j info", new ProcessResult(1, "", "the service is not running"));
    }

    private static string IcaclsArguments => ZeroTierTokenGrant.ArgumentsFor(TokenPath, Sid);

    private ZeroTierCheck Check() => new(
        _zeroTier,
        _config,
        new ZeroTierFaultReader(_runner, new ZeroTierCommand(_commandFile, Folders)),
        new ZeroTierTokenGrant(_grantRunner ?? _elevated, () => _accountId, () => SharedData));

    /// <summary>
    /// This method puts ZeroTier into the state of the real Windows run: the
    /// command runs, and ZeroTier cannot read its own token.
    /// </summary>
    private void ZeroTierRefusesTheCommand()
    {
        _zeroTier.Status = null;
        _runner.Returns(
            Command,
            "-j info",
            new ProcessResult(ZeroTierFaultReader.TokenExitCode, TokenText, ""));
    }

    /// <summary>
    /// This runner stands for the elevated runner, and it changes the state of
    /// ZeroTier after the command. A real icacls changes the permission, and
    /// the next zerotier-cli then works.
    /// </summary>
    private sealed class RunnerThatChangesTheState : IProcessRunner
    {
        private readonly FakeElevatedProcessRunner _inner;
        private readonly Action _afterTheRun;

        public RunnerThatChangesTheState(
            FakeElevatedProcessRunner inner, Action afterTheRun)
        {
            _inner = inner;
            _afterTheRun = afterTheRun;
        }

        public async Task<ProcessResult> RunAsync(
            string fileName, string arguments, CancellationToken ct)
        {
            var result = await _inner.RunAsync(fileName, arguments, ct);
            _afterTheRun();
            return result;
        }
    }

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
                _runner, new ZeroTierCommand("zerotier-cli", Array.Empty<string>())),
            new ZeroTierTokenGrant(_elevated, () => _accountId, () => SharedData));

        var result = await check.RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("did not find the ZeroTier command", result.Message);
        Assert.Contains("found no folder of programs", result.FixDescription);
        Assert.Contains("https://www.zerotier.com/download/", result.FixDescription);
    }

    [Fact]
    public async Task Names_the_token_when_ZeroTier_refuses_the_command()
    {
        ZeroTierRefusesTheCommand();

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("authentication token", result.Message);

        // ZeroTier runs. So the text must not send the player to the download
        // page, and it must not send the player to the Services window.
        Assert.DoesNotContain("https://www.zerotier.com/download/", result.FixDescription);
        Assert.DoesNotContain("Services", result.FixDescription);
    }

    /// <summary>
    /// This is the one Fix of the app that changes a permission on a secret.
    /// So the advice must say what the Fix will change, before the player
    /// presses it.
    /// </summary>
    [Fact]
    public async Task The_advice_for_the_token_says_what_the_Fix_will_change()
    {
        ZeroTierRefusesTheCommand();

        var result = await Check().RunAsync(default);

        Assert.Contains("read the token file", result.FixDescription);
        Assert.Contains("one time", result.FixDescription);
        Assert.Contains("this account only", result.FixDescription);
        Assert.Contains("nothing else", result.FixDescription);
        Assert.Contains("does not read the token", result.FixDescription);

        // A player who would rather not change a permission keeps the other
        // way out.
        Assert.Contains("run MM2 Lobby as an administrator", result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_gives_the_read_permission_to_this_account_only()
    {
        ZeroTierRefusesTheCommand();
        _elevated.Returns(ZeroTierTokenGrant.Program, IcaclsArguments, 0);

        // A real icacls changes the permission, and the next zerotier-cli then
        // works.
        _grantRunner = new RunnerThatChangesTheState(
            _elevated, () => _zeroTier.Status = new ZeroTierStatus(true, "a09acf0233"));

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(
            new[] { $"icacls.exe \"{TokenPath}\" /grant *{Sid}:(R)" }, _elevated.Calls);
    }

    /// <summary>
    /// An exit code of 0 proves that icacls ran. It does not prove that
    /// ZeroTier answers now. So the Fix runs the real command again, and it
    /// reports the answer of that command.
    /// </summary>
    [Fact]
    public async Task FixAsync_runs_the_real_command_again_and_reports_its_answer()
    {
        ZeroTierRefusesTheCommand();
        _elevated.Returns(ZeroTierTokenGrant.Program, IcaclsArguments, 0);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("ZeroTier still refuses the command", result.Message);
        Assert.Contains("run MM2 Lobby as an administrator", result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_blames_the_permission_prompt_when_Windows_ran_nothing()
    {
        ZeroTierRefusesTheCommand();
        _elevated.Returns(
            ZeroTierTokenGrant.Program, IcaclsArguments, SystemProcessRunner.NotRunExitCode);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("Windows did not run the command", result.Message);
        Assert.Contains("chose No at the permission prompt", result.Message);
        Assert.Contains($"icacls \"{TokenPath}\" /grant *{Sid}:(R)", result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_reports_a_command_that_ran_and_failed()
    {
        ZeroTierRefusesTheCommand();
        _elevated.Returns(ZeroTierTokenGrant.Program, IcaclsArguments, 5);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("The command failed", result.Message);
        Assert.Contains("exit code 5", result.Message);
    }

    [Fact]
    public async Task FixAsync_changes_no_permission_when_the_app_cannot_read_the_account()
    {
        ZeroTierRefusesTheCommand();
        _accountId = null;

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("cannot read the ID of your Windows account", result.Message);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task FixAsync_changes_no_permission_when_ZeroTier_gives_no_answer()
    {
        // The window shows Fix beside every failed row. The state can change
        // between the check and the Fix. So the Fix reads the reason again,
        // and it changes a permission for the one reason that needs it.
        _zeroTier.Status = null;

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("type Services", result.FixDescription);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task FixAsync_changes_no_permission_when_the_app_found_no_command()
    {
        _zeroTier.Status = null;
        _commandFile = "zerotier-cli";

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("did not find the ZeroTier command", result.Message);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task FixAsync_changes_no_permission_while_ZeroTier_answers()
    {
        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Empty(_elevated.Calls);
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
