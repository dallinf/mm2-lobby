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

    private const string Scope =
        "The check sees only the two rules that this app adds. If Windows already has an Allow "
        + "rule for MM2, pressing Fix adds the app's own rules.";

    private static readonly string ShowInbound = Show(InboundName);
    private static readonly string ShowOutbound = Show(OutboundName);

    private readonly FakeProcessRunner _plain = new();
    private readonly FakeElevatedProcessRunner _elevated = new();
    private readonly AppConfig _config = new() { GamePath = GamePath };

    private FirewallCheck Check() => new(_plain, _elevated, _config);

    private static string Show(string name)
        => $"{Program} advfirewall firewall show rule name=\"{name}\"";

    private static string AddInbound(string path = GamePath)
        => $"{Program} advfirewall firewall add rule name=\"{InboundName}\" "
            + $"dir=in action=allow program=\"{path}\" enable=yes profile=any";

    private static string AddOutbound(string path = GamePath)
        => $"{Program} advfirewall firewall add rule name=\"{OutboundName}\" "
            + $"dir=out action=allow program=\"{path}\" enable=yes profile=any";

    private static string SetInbound(string path = GamePath)
        => $"{Program} advfirewall firewall set rule name=\"{InboundName}\" dir=in "
            + $"new program=\"{path}\"";

    private static string SetOutbound(string path = GamePath)
        => $"{Program} advfirewall firewall set rule name=\"{OutboundName}\" dir=out "
            + $"new program=\"{path}\"";

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
        Assert.Contains("outbound traffic", result.Message);
        Assert.DoesNotContain("inbound", result.Message);
        Assert.Contains(Scope, result.Message);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task Fails_and_names_the_inbound_rule_when_only_the_outbound_rule_exists()
    {
        Missing(InboundName);
        Listed(OutboundName);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("inbound traffic", result.Message);
        Assert.DoesNotContain("outbound", result.Message);
        Assert.Contains(Scope, result.Message);
    }

    [Fact]
    public async Task Fails_and_names_both_rules_when_neither_exists()
    {
        Missing(InboundName);
        Missing(OutboundName);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("rules are missing", result.Message);
        Assert.Contains(Scope, result.Message);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task Says_only_what_it_knows_and_does_not_claim_that_MM2_is_blocked()
    {
        Missing(InboundName);
        Missing(OutboundName);

        var result = await Check().RunAsync(default);

        Assert.Contains("MM2 Lobby firewall rule", result.Message);
        Assert.DoesNotContain("No firewall rule allows MM2", result.Message);
        Assert.Contains("The check sees only the two rules that this app adds", result.Message);
        Assert.Contains(
            "If Windows already has an Allow rule for MM2, pressing Fix adds the app's own rules",
            result.Message);
        Assert.DoesNotContain("skip", result.Message);
    }

    [Fact]
    public async Task Tells_the_player_why_the_rules_matter_and_how_many_prompts_to_expect()
    {
        Missing(InboundName);
        Missing(OutboundName);

        var result = await Check().RunAsync(default);

        Assert.Contains("no host", result.FixDescription);
        Assert.Contains("Windows will ask for permission up to two times", result.FixDescription);
        Assert.Contains("Choose Yes each time", result.FixDescription);
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
        Assert.Contains("outbound traffic", result.Message);
    }

    // The fix tests use FakeFirewall, which holds the rule table. A fix that
    // changes a rule must change it in that table, and the final check reads it.
    private FirewallCheck CheckOn(FakeFirewall firewall) => new(firewall.Plain, firewall.Elevated, _config);

    private static FakeFirewall.Rule Rule(string name, string direction, string program = GamePath)
        => new(name, direction, program);

    private static FakeFirewall WithRules(bool inbound, bool outbound, string program = GamePath)
    {
        var firewall = new FakeFirewall();
        if (inbound) firewall.Rules.Add(Rule(InboundName, "in", program));
        if (outbound) firewall.Rules.Add(Rule(OutboundName, "out", program));
        return firewall;
    }

    [Fact]
    public async Task FixAsync_adds_both_rules_when_no_rule_exists()
    {
        var firewall = WithRules(inbound: false, outbound: false);

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Equal(new[] { AddInbound(), AddOutbound() }, firewall.ElevatedCalls);
        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Fact]
    public async Task FixAsync_changes_the_inbound_rule_and_adds_the_outbound_rule_when_only_the_inbound_rule_exists()
    {
        var firewall = WithRules(inbound: true, outbound: false);

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Equal(new[] { SetInbound(), AddOutbound() }, firewall.ElevatedCalls);
        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Fact]
    public async Task FixAsync_adds_the_inbound_rule_and_changes_the_outbound_rule_when_only_the_outbound_rule_exists()
    {
        var firewall = WithRules(inbound: false, outbound: true);

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Equal(new[] { AddInbound(), SetOutbound() }, firewall.ElevatedCalls);
        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Fact]
    public async Task FixAsync_changes_both_rules_in_place_when_both_rules_exist()
    {
        var firewall = WithRules(inbound: true, outbound: true);

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Equal(new[] { SetInbound(), SetOutbound() }, firewall.ElevatedCalls);
        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FixAsync_asks_for_permission_exactly_two_times(bool inbound, bool outbound)
    {
        var firewall = WithRules(inbound, outbound);

        await CheckOn(firewall).FixAsync(default);

        Assert.Equal(2, firewall.ElevatedCalls.Count());
    }

    [Fact]
    public async Task FixAsync_gives_a_rule_that_names_an_old_program_path_the_new_path_and_never_deletes_it()
    {
        const string old = @"D:\Old\midtown2.exe";
        var firewall = WithRules(inbound: true, outbound: true, program: old);

        await CheckOn(firewall).FixAsync(default);

        Assert.Equal(2, firewall.Rules.Count);
        Assert.All(firewall.Rules, rule => Assert.Equal(GamePath, rule.Program));
        Assert.DoesNotContain(firewall.Log, call => call.Contains("delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FixAsync_asks_what_exists_with_the_plain_runner_before_it_starts_an_elevated_process()
    {
        var firewall = new FakeFirewall();

        await CheckOn(firewall).FixAsync(default);

        Assert.Equal(
            new[] { $"plain: {ShowInbound}", $"plain: {ShowOutbound}" },
            firewall.Log.Take(2));
    }

    [Fact]
    public async Task FixAsync_checks_both_rules_with_the_plain_runner_after_the_last_change()
    {
        var firewall = new FakeFirewall();

        await CheckOn(firewall).FixAsync(default);

        Assert.Equal(
            new[] { $"elevated: {AddOutbound()}", $"plain: {ShowInbound}", $"plain: {ShowOutbound}" },
            firewall.Log.TakeLast(3));
    }

    [Fact]
    public async Task FixAsync_says_something_new_when_netsh_accepts_the_commands_but_the_rules_are_still_missing()
    {
        var firewall = new FakeFirewall { AddsAreIgnored = true };
        var check = CheckOn(firewall);
        var before = await check.RunAsync(default);

        var result = await check.FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(2, firewall.ElevatedCalls.Count());
        Assert.Contains("accepted the commands", result.Message);
        Assert.NotEqual(before.Message, result.Message);
        Assert.NotEqual(before.FixDescription, result.FixDescription);
        Assert.Contains("Check again", result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_names_the_inbound_rule_and_blames_the_prompt_when_a_command_returns_127()
    {
        var firewall = new FakeFirewall();
        firewall.AddFails[InboundName] = 127;

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("did not add the inbound rule", result.Message);
        Assert.DoesNotContain("in rule", result.Message);
        Assert.Contains("exit code 127", result.Message);
        Assert.Contains("If you chose No at the permission prompt, that is the cause", result.Message);
        Assert.Contains("Press Fix again", result.FixDescription);
        Assert.Contains("netsh " + Arguments(AddInbound()), result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_reports_the_exit_code_and_does_not_blame_the_prompt_for_another_code()
    {
        var firewall = new FakeFirewall();
        firewall.AddFails[OutboundName] = 5;

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("did not add the outbound rule", result.Message);
        Assert.DoesNotContain("out rule", result.Message);
        Assert.Contains("exit code 5", result.Message);
        Assert.Contains("The command failed", result.Message);
        Assert.DoesNotContain("permission prompt", result.Message);
        Assert.Contains("netsh " + Arguments(AddOutbound()), result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_names_the_outbound_rule_when_netsh_cannot_change_it()
    {
        var firewall = WithRules(inbound: true, outbound: true);
        firewall.SetFails[OutboundName] = 127;

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("did not update the outbound rule", result.Message);
        Assert.Contains("exit code 127", result.Message);
        Assert.Contains("netsh " + Arguments(SetOutbound()), result.FixDescription);
        Assert.Equal(new[] { SetInbound(), SetOutbound() }, firewall.ElevatedCalls);
    }

    [Fact]
    public async Task FixAsync_names_the_inbound_rule_when_netsh_cannot_change_it_and_leaves_the_outbound_rule_alone()
    {
        var firewall = WithRules(inbound: true, outbound: true);
        firewall.SetFails[InboundName] = 127;

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Equal(new[] { SetInbound() }, firewall.ElevatedCalls);
        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("did not update the inbound rule", result.Message);
    }

    [Fact]
    public async Task FixAsync_keeps_the_changed_inbound_rule_and_the_old_outbound_rule_when_netsh_cannot_change_the_outbound_rule()
    {
        const string old = @"D:\Old\midtown2.exe";
        var firewall = WithRules(inbound: true, outbound: true, program: old);
        firewall.SetFails[OutboundName] = 127;

        await CheckOn(firewall).FixAsync(default);

        Assert.Equal(GamePath, firewall.Rules.Single(rule => rule.Name == InboundName).Program);
        Assert.Equal(old, firewall.Rules.Single(rule => rule.Name == OutboundName).Program);
    }

    [Fact]
    public async Task FixAsync_keeps_the_changed_inbound_rule_when_netsh_cannot_add_the_outbound_rule()
    {
        const string old = @"D:\Old\midtown2.exe";
        var firewall = WithRules(inbound: true, outbound: false, program: old);
        firewall.AddFails[OutboundName] = 127;

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(new[] { SetInbound(), AddOutbound() }, firewall.ElevatedCalls);
        Assert.Equal(GamePath, Assert.Single(firewall.Rules).Program);
    }

    // FakeFirewall deletes a rule for a delete command, as netsh does. So a fix
    // that deletes a rule before it changes the rule fails here.
    [Theory]
    [InlineData(false, false, InboundName)]
    [InlineData(false, false, OutboundName)]
    [InlineData(true, false, InboundName)]
    [InlineData(true, false, OutboundName)]
    [InlineData(false, true, InboundName)]
    [InlineData(false, true, OutboundName)]
    [InlineData(true, true, InboundName)]
    [InlineData(true, true, OutboundName)]
    public async Task FixAsync_never_leaves_fewer_rules_than_it_found_when_a_command_fails(
        bool inbound, bool outbound, string failingRule)
    {
        var firewall = WithRules(inbound, outbound);
        firewall.AddFails[failingRule] = 127;
        firewall.SetFails[failingRule] = 127;
        var found = firewall.Rules.Count;

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.True(firewall.Rules.Count >= found, "A failed fix removed a rule.");
        Assert.True(!inbound || firewall.Rules.Any(rule => rule.Name == InboundName));
        Assert.True(!outbound || firewall.Rules.Any(rule => rule.Name == OutboundName));
    }

    [Fact]
    public async Task FixAsync_does_not_touch_the_outbound_rule_after_the_inbound_rule_fails()
    {
        var firewall = WithRules(inbound: false, outbound: true);
        firewall.AddFails[InboundName] = 127;

        await CheckOn(firewall).FixAsync(default);

        Assert.Equal(new[] { AddInbound() }, firewall.ElevatedCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FixAsync_starts_no_process_when_the_game_path_is_blank(string path)
    {
        _config.GamePath = path;
        var firewall = new FakeFirewall();

        var result = await CheckOn(firewall).FixAsync(default);

        Assert.Empty(firewall.Log);
        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("game path", result.Message);
        Assert.Contains("Midtown Madness 2 check", result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_reads_the_game_path_from_the_shared_configuration_on_every_call()
    {
        var firewall = new FakeFirewall();
        var check = CheckOn(firewall);
        const string moved = @"D:\Games\MM2\midtown2.exe";
        _config.GamePath = moved;

        var result = await check.FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(new[] { AddInbound(moved), AddOutbound(moved) }, firewall.ElevatedCalls);
    }

    [Fact]
    public void Has_the_name_Windows_Firewall()
    {
        Assert.Equal("Windows Firewall", Check().Name);
    }
}
