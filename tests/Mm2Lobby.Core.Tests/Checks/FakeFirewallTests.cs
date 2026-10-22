using Mm2Lobby.Core.Tests.Fakes;

namespace Mm2Lobby.Core.Tests.Checks;

public class FakeFirewallTests
{
    private const string Show = "advfirewall firewall show rule name=\"R\"";
    private const string Delete = "advfirewall firewall delete rule name=\"R\"";
    private const string Add =
        "advfirewall firewall add rule name=\"R\" dir=in action=allow program=\"C:\\g\\m.exe\" enable=yes profile=any";

    private readonly FakeFirewall _firewall = new();

    [Fact]
    public async Task Plain_show_lists_an_existing_rule_by_name()
    {
        await _firewall.Elevated.RunAsync("netsh.exe", Add, default);

        var result = await _firewall.Plain.RunAsync("netsh.exe", Show, default);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Rule Name:   R", result.StandardOutput);
    }

    [Fact]
    public async Task Plain_show_answers_exit_code_1_when_no_rule_exists()
    {
        var result = await _firewall.Plain.RunAsync("netsh.exe", Show, default);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task Plain_add_and_delete_are_refused_and_change_nothing()
    {
        await _firewall.Elevated.RunAsync("netsh.exe", Add, default);

        var add = await _firewall.Plain.RunAsync("netsh.exe", Add, default);
        var delete = await _firewall.Plain.RunAsync("netsh.exe", Delete, default);

        Assert.Equal(1, add.ExitCode);
        Assert.Equal(1, delete.ExitCode);
        Assert.Single(_firewall.Rules);
    }

    [Fact]
    public async Task Elevated_show_succeeds_and_returns_no_output()
    {
        await _firewall.Elevated.RunAsync("netsh.exe", Add, default);

        var result = await _firewall.Elevated.RunAsync("netsh.exe", Show, default);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public async Task Elevated_show_keeps_the_exit_code_of_netsh_and_still_returns_no_output()
    {
        var result = await _firewall.Elevated.RunAsync("netsh.exe", Show, default);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public async Task Elevated_delete_removes_the_rule_and_answers_1_when_none_exists()
    {
        await _firewall.Elevated.RunAsync("netsh.exe", Add, default);

        var first = await _firewall.Elevated.RunAsync("netsh.exe", Delete, default);
        var second = await _firewall.Elevated.RunAsync("netsh.exe", Delete, default);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(1, second.ExitCode);
        Assert.Empty(_firewall.Rules);
    }

    [Fact]
    public async Task Adding_a_rule_twice_leaves_two_rules()
    {
        await _firewall.Elevated.RunAsync("netsh.exe", Add, default);
        await _firewall.Elevated.RunAsync("netsh.exe", Add, default);

        Assert.Equal(2, _firewall.Rules.Count);
    }

    [Fact]
    public async Task Throws_for_an_empty_file_name_and_answers_127_for_another_program()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _firewall.Plain.RunAsync("", Show, default));

        var other = await _firewall.Plain.RunAsync("other.exe", Show, default);
        Assert.Equal(127, other.ExitCode);
    }

    [Fact]
    public async Task Answers_exit_code_1_for_a_command_that_netsh_does_not_know()
    {
        var result = await _firewall.Elevated.RunAsync("netsh.exe", "advfirewall firewall nonsense", default);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task Logs_each_call_with_the_runner_that_made_it()
    {
        await _firewall.Plain.RunAsync("netsh.exe", Show, default);
        await _firewall.Elevated.RunAsync("netsh.exe", Add, default);

        Assert.Equal(new[] { $"plain: netsh.exe {Show}", $"elevated: netsh.exe {Add}" }, _firewall.Log);
        Assert.Equal(new[] { $"netsh.exe {Show}" }, _firewall.PlainCalls);
        Assert.Equal(new[] { $"netsh.exe {Add}" }, _firewall.ElevatedCalls);
    }
}
