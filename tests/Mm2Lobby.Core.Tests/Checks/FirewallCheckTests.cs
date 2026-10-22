using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

public class FirewallCheckTests
{
    private const string GamePath = @"C:\Games\Midtown Madness 2\midtown2.exe";

    private const string InboundName = "MM2 Lobby - Midtown Madness 2 (inbound)";
    private const string OutboundName = "MM2 Lobby - Midtown Madness 2 (outbound)";

    private const string Program = "netsh.exe";

    private static readonly string ShowInbound = Show(InboundName);
    private static readonly string ShowOutbound = Show(OutboundName);
    private static readonly string DeleteInbound = Delete(InboundName);
    private static readonly string DeleteOutbound = Delete(OutboundName);

    private readonly FakeProcessRunner _plain = new();
    private readonly FakeElevatedProcessRunner _elevated = new();
    private readonly AppConfig _config = new() { GamePath = GamePath };

    private FirewallCheck Check() => new(_plain, _elevated, _config);

    private static string Show(string name)
        => $"{Program} advfirewall firewall show rule name=\"{name}\"";

    private static string Delete(string name)
        => $"{Program} advfirewall firewall delete rule name=\"{name}\"";

    private static string AddInbound(string path = GamePath)
        => $"{Program} advfirewall firewall add rule name=\"{InboundName}\" "
            + $"dir=in action=allow program=\"{path}\" enable=yes profile=any";

    private static string AddOutbound(string path = GamePath)
        => $"{Program} advfirewall firewall add rule name=\"{OutboundName}\" "
            + $"dir=out action=allow program=\"{path}\" enable=yes profile=any";

    private static string Arguments(string call) => call[(Program.Length + 1)..];

    /// <summary>The plain runner lists the rule, as netsh does for an existing rule.</summary>
    private void Listed(string name)
        => _plain.Returns(Program, Arguments(Show(name)), new ProcessResult(
            0, $"Rule Name:                            {name}\r\n", ""));

    /// <summary>The plain runner finds no rule, as netsh does: exit code 1.</summary>
    private void Missing(string name)
        => _plain.Returns(Program, Arguments(Show(name)), new ProcessResult(
            1, "No rules match the specified criteria.\r\n", ""));

    private void BothListed()
    {
        Listed(InboundName);
        Listed(OutboundName);
    }

    /// <summary>
    /// The elevated runner accepts both deletes and both adds. The delete
    /// answers with deleteExitCode: netsh answers 1 when no rule exists.
    /// </summary>
    private void FixWorks(int deleteExitCode = 1)
    {
        _elevated
            .Returns(Program, Arguments(DeleteInbound), deleteExitCode)
            .Returns(Program, Arguments(AddInbound()), 0)
            .Returns(Program, Arguments(DeleteOutbound), deleteExitCode)
            .Returns(Program, Arguments(AddOutbound()), 0);
    }

    [Fact]
    public void The_rule_names_are_the_names_the_group_documents()
    {
        Assert.Equal("MM2 Lobby - Midtown Madness 2", FirewallCheck.RuleName);
        Assert.Equal(InboundName, FirewallCheck.InboundRuleName);
        Assert.Equal(OutboundName, FirewallCheck.OutboundRuleName);
    }

    [Fact]
    public async Task Passes_when_netsh_lists_both_rules()
    {
        BothListed();

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Null(result.FixDescription);
        Assert.Equal(new[] { ShowInbound, ShowOutbound }, _plain.Calls);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task Fails_and_names_the_outbound_rule_when_only_the_inbound_rule_exists()
    {
        Listed(InboundName);
        Missing(OutboundName);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("outbound", result.Message);
        Assert.DoesNotContain("inbound", result.Message);
        Assert.Contains("no host", result.Message);
        Assert.Contains("administrator", result.FixDescription);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task Fails_and_names_the_inbound_rule_when_only_the_outbound_rule_exists()
    {
        Missing(InboundName);
        Listed(OutboundName);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("inbound", result.Message);
        Assert.DoesNotContain("outbound", result.Message);
        Assert.Contains("no host", result.Message);
        Assert.Contains("administrator", result.FixDescription);
    }

    [Fact]
    public async Task Fails_and_names_both_rules_when_neither_exists()
    {
        Missing(InboundName);
        Missing(OutboundName);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("inbound", result.Message);
        Assert.Contains("outbound", result.Message);
        Assert.Contains("no host", result.Message);
        Assert.Contains("administrator", result.FixDescription);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task Asks_about_the_outbound_rule_even_when_the_inbound_rule_is_missing()
    {
        Missing(InboundName);
        Missing(OutboundName);

        await Check().RunAsync(default);

        Assert.Equal(new[] { ShowInbound, ShowOutbound }, _plain.Calls);
    }

    [Fact]
    public async Task Fails_when_netsh_succeeds_but_lists_only_other_rules()
    {
        _plain.Returns(Program, Arguments(ShowInbound),
            new ProcessResult(0, "Rule Name:   Some Other Rule\r\n", ""));
        Listed(OutboundName);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
    }

    [Fact]
    public async Task Fails_when_netsh_fails_even_if_the_output_names_the_rule()
    {
        _plain.Returns(Program, Arguments(ShowInbound), new ProcessResult(1, InboundName, ""));
        Listed(OutboundName);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
    }

    [Fact]
    public async Task Judges_each_answer_by_the_name_of_its_own_rule()
    {
        // Both answers name the inbound rule only. The outbound answer must
        // not pass because the inbound name is in the output.
        Listed(InboundName);
        _plain.Returns(Program, Arguments(ShowOutbound), new ProcessResult(
            0, $"Rule Name:   {InboundName}\r\n", ""));

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("outbound", result.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task FixAsync_deletes_each_rule_then_adds_it_and_ignores_the_delete_exit_code(
        int deleteExitCode)
    {
        FixWorks(deleteExitCode);
        BothListed();

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(
            new[] { DeleteInbound, AddInbound(), DeleteOutbound, AddOutbound() },
            _elevated.Calls);
    }

    [Fact]
    public async Task FixAsync_checks_with_the_plain_runner_after_the_rules_are_added()
    {
        FixWorks();
        BothListed();

        await Check().FixAsync(default);

        Assert.Equal(new[] { ShowInbound, ShowOutbound }, _plain.Calls);
    }

    [Fact]
    public async Task FixAsync_reports_a_failure_when_the_rules_were_added_but_netsh_does_not_list_them()
    {
        FixWorks();
        Missing(InboundName);
        Missing(OutboundName);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(4, _elevated.Calls.Count);
    }

    [Fact]
    public async Task FixAsync_reports_the_exit_code_and_the_manual_command_when_the_inbound_rule_fails()
    {
        FixWorks();
        _elevated.Returns(Program, Arguments(AddInbound()), 1);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("exit code 1", result.Message);
        Assert.Contains("in rule", result.Message);
        Assert.Contains("administrator prompt", result.Message);
        Assert.Contains("netsh " + Arguments(AddInbound()), result.FixDescription);
        Assert.Empty(_plain.Calls);
    }

    [Fact]
    public async Task FixAsync_does_not_touch_the_outbound_rule_after_the_inbound_rule_fails()
    {
        FixWorks();
        _elevated.Returns(Program, Arguments(AddInbound()), 1);

        await Check().FixAsync(default);

        Assert.Equal(new[] { DeleteInbound, AddInbound() }, _elevated.Calls);
    }

    [Fact]
    public async Task FixAsync_names_the_outbound_rule_when_only_that_rule_fails()
    {
        FixWorks();
        _elevated.Returns(Program, Arguments(AddOutbound()), 5);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("exit code 5", result.Message);
        Assert.Contains("out rule", result.Message);
        Assert.Contains("netsh " + Arguments(AddOutbound()), result.FixDescription);
        Assert.Equal(
            new[] { DeleteInbound, AddInbound(), DeleteOutbound, AddOutbound() },
            _elevated.Calls);
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
            .Returns(Program, Arguments(AddInbound(moved)), 0)
            .Returns(Program, Arguments(AddOutbound(moved)), 0);
        BothListed();

        var result = await check.FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(
            new[] { DeleteInbound, AddInbound(moved), DeleteOutbound, AddOutbound(moved) },
            _elevated.Calls);
    }

    [Fact]
    public void Has_the_name_Windows_Firewall()
    {
        Assert.Equal("Windows Firewall", Check().Name);
    }
}
