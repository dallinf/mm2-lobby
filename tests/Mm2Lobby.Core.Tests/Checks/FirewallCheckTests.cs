using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

public class FirewallCheckTests
{
    private const string GamePath = @"C:\Games\Midtown Madness 2\midtown2.exe";

    private const string ShowRule =
        "netsh.exe advfirewall firewall show rule name=\"MM2 Lobby - Midtown Madness 2\"";

    private readonly FakeProcessRunner _plain = new();
    private readonly FakeElevatedProcessRunner _elevated = new();
    private readonly AppConfig _config = new() { GamePath = GamePath };

    private FirewallCheck Check() => new(_plain, _elevated, _config);

    private static string AddRule(string direction, string path = GamePath)
        => "netsh.exe advfirewall firewall add rule name=\"MM2 Lobby - Midtown Madness 2\" "
            + $"dir={direction} action=allow program=\"{path}\" enable=yes profile=any";

    private const string Program = "netsh.exe";

    private static string Arguments(string call) => call[(Program.Length + 1)..];

    private void RuleExists()
        => _plain.Returns("netsh.exe", Arguments(ShowRule), new ProcessResult(
            0,
            "Rule Name:                            MM2 Lobby - Midtown Madness 2\r\n",
            ""));

    private void ElevatedAddSucceeds(string direction, int exitCode = 0)
    {
        _elevated.Returns(Program, Arguments(AddRule(direction)), exitCode);
    }

    [Fact]
    public void The_rule_name_is_the_name_the_group_documents()
    {
        Assert.Equal("MM2 Lobby - Midtown Madness 2", FirewallCheck.RuleName);
    }

    [Fact]
    public async Task Passes_when_netsh_lists_the_rule()
    {
        RuleExists();

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Null(result.FixDescription);
        Assert.Equal(new[] { ShowRule }, _plain.Calls);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task Fails_when_netsh_finds_no_rule_and_says_why_it_matters()
    {
        _plain.Returns(
            "netsh.exe",
            Arguments(ShowRule),
            new ProcessResult(1, "No rules match the specified criteria.\r\n", ""));

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("no host", result.Message);
        Assert.Contains("administrator", result.FixDescription);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task Fails_when_netsh_succeeds_but_lists_only_other_rules()
    {
        _plain.Returns(
            "netsh.exe",
            Arguments(ShowRule),
            new ProcessResult(0, "Rule Name:   Some Other Rule\r\n", ""));

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
    }

    [Fact]
    public async Task Fails_when_netsh_fails_even_if_the_output_names_the_rule()
    {
        _plain.Returns(
            "netsh.exe",
            Arguments(ShowRule),
            new ProcessResult(1, "MM2 Lobby - Midtown Madness 2", ""));

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
    }

    [Fact]
    public async Task FixAsync_adds_the_inbound_rule_then_the_outbound_rule_through_the_elevated_runner()
    {
        ElevatedAddSucceeds("in");
        ElevatedAddSucceeds("out");
        RuleExists();

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(new[] { AddRule("in"), AddRule("out") }, _elevated.Calls);
    }

    [Fact]
    public async Task FixAsync_checks_with_the_plain_runner_after_the_rules_are_added()
    {
        ElevatedAddSucceeds("in");
        ElevatedAddSucceeds("out");
        RuleExists();

        await Check().FixAsync(default);

        Assert.Equal(new[] { ShowRule }, _plain.Calls);
    }

    [Fact]
    public async Task FixAsync_reports_a_failure_when_the_rules_were_added_but_netsh_does_not_list_them()
    {
        ElevatedAddSucceeds("in");
        ElevatedAddSucceeds("out");

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(2, _elevated.Calls.Count);
    }

    [Fact]
    public async Task FixAsync_reports_the_exit_code_and_the_manual_command_when_the_inbound_rule_fails()
    {
        ElevatedAddSucceeds("in", exitCode: 1);
        ElevatedAddSucceeds("out");

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("exit code 1", result.Message);
        Assert.Contains("in rule", result.Message);
        Assert.Contains("administrator prompt", result.Message);
        Assert.Contains("netsh " + Arguments(AddRule("in")), result.FixDescription);
        Assert.Empty(_plain.Calls);
    }

    [Fact]
    public async Task FixAsync_does_not_add_the_outbound_rule_after_the_inbound_rule_fails()
    {
        ElevatedAddSucceeds("in", exitCode: 1);
        ElevatedAddSucceeds("out");

        await Check().FixAsync(default);

        Assert.Equal(new[] { AddRule("in") }, _elevated.Calls);
    }

    [Fact]
    public async Task FixAsync_names_the_outbound_rule_when_only_that_rule_fails()
    {
        ElevatedAddSucceeds("in");
        ElevatedAddSucceeds("out", exitCode: 5);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("exit code 5", result.Message);
        Assert.Contains("out rule", result.Message);
        Assert.Equal(new[] { AddRule("in"), AddRule("out") }, _elevated.Calls);
        Assert.Empty(_plain.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FixAsync_starts_no_process_when_the_game_path_is_blank(string path)
    {
        _config.GamePath = path;

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Empty(_elevated.Calls);
        Assert.Empty(_plain.Calls);
        Assert.Contains("game path", result.Message);
        Assert.Contains("Midtown Madness 2 check", result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_reads_the_game_path_from_the_shared_configuration_on_every_call()
    {
        var check = Check();
        const string moved = @"D:\Games\MM2\midtown2.exe";
        _config.GamePath = moved;
        _elevated
            .Returns(Program, Arguments(AddRule("in", moved)), 0)
            .Returns(Program, Arguments(AddRule("out", moved)), 0);
        RuleExists();

        var result = await check.FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(new[] { AddRule("in", moved), AddRule("out", moved) }, _elevated.Calls);
    }

    [Fact]
    public void Has_the_name_Windows_Firewall()
    {
        Assert.Equal("Windows Firewall", Check().Name);
    }
}
