using Mm2Lobby.Core.Tests.Fakes;

namespace Mm2Lobby.Core.Tests.Checks;

public class FakeFirewallTests
{
    private const string Show = "advfirewall firewall show rule name=\"R\"";

    private const string Add =
        "advfirewall firewall add rule name=\"R\" dir=in action=allow program=\"C:\\g\\m.exe\" "
        + "enable=yes profile=any";

    private const string Set =
        "advfirewall firewall set rule name=\"R\" dir=in new program=\"D:\\h\\m.exe\" "
        + "action=allow enable=yes profile=any";

    private readonly FakeFirewall _firewall = new();

    private async Task<int> Run(string arguments)
        => (await _firewall.Elevated.RunAsync("netsh.exe", arguments, default)).ExitCode;

    [Fact]
    public async Task Plain_show_lists_an_existing_rule_by_name()
    {
        await Run(Add);

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

    [Theory]
    [InlineData(Add)]
    [InlineData(Set)]
    public async Task Plain_add_and_set_are_refused_and_change_nothing(string command)
    {
        await Run(Add);
        var before = _firewall.Rules.ToList();

        var result = await _firewall.Plain.RunAsync("netsh.exe", command, default);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(before, _firewall.Rules);
    }

    [Fact]
    public async Task Elevated_show_keeps_the_exit_code_of_netsh_and_returns_no_output()
    {
        await Run(Add);

        var found = await _firewall.Elevated.RunAsync("netsh.exe", Show, default);
        _firewall.Rules.Clear();
        var missing = await _firewall.Elevated.RunAsync("netsh.exe", Show, default);

        Assert.Equal(0, found.ExitCode);
        Assert.Equal("", found.StandardOutput);
        Assert.Equal(1, missing.ExitCode);
        Assert.Equal("", missing.StandardOutput);
    }

    [Fact]
    public async Task Adding_a_rule_twice_leaves_two_rules()
    {
        await Run(Add);
        await Run(Add);

        Assert.Equal(2, _firewall.Rules.Count);
    }

    [Theory]
    [InlineData("advfirewall firewall add rule name=\"R\"")]
    [InlineData("advfirewall firewall add rule name=\"R\" action=allow program=\"C:\\g\\m.exe\"")]
    [InlineData("advfirewall firewall add rule name=\"R\" dir=in program=\"C:\\g\\m.exe\"")]
    [InlineData("advfirewall firewall add rule name=\"R\" dir=in action=allow")]
    public async Task Add_without_dir_action_or_program_answers_1_and_creates_no_rule(string command)
    {
        Assert.Equal(1, await Run(command));
        Assert.Empty(_firewall.Rules);
    }

    [Theory]
    [InlineData("advfirewall firewall show rule name=\"R\" dir=in")]
    [InlineData("advfirewall firewall show rule name=\"R\" dir=in action=allow program=\"C:\\g\\m.exe\"")]
    public async Task Show_with_a_parameter_that_only_add_takes_answers_1(string command)
    {
        await Run(Add);

        var result = await _firewall.Plain.RunAsync("netsh.exe", command, default);

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("Rule Name", result.StandardOutput);
    }

    [Fact]
    public async Task Set_changes_the_program_of_the_rule_in_place()
    {
        await Run(Add);

        Assert.Equal(0, await Run(Set));

        var rule = Assert.Single(_firewall.Rules);
        Assert.Equal(new FakeFirewall.Rule("R", "in", @"D:\h\m.exe"), rule);
    }

    [Fact]
    public async Task Set_keeps_the_rule_at_its_place_in_the_table()
    {
        await Run(Add);
        await Run(Add.Replace("name=\"R\"", "name=\"S\"").Replace("dir=in", "dir=out"));

        await Run(Set);

        Assert.Equal(new[] { "R", "S" }, _firewall.Rules.Select(rule => rule.Name));
        Assert.Equal(@"D:\h\m.exe", _firewall.Rules[0].Program);
        Assert.Equal(@"C:\g\m.exe", _firewall.Rules[1].Program);
    }

    [Fact]
    public async Task Set_answers_1_and_changes_nothing_when_no_rule_matches_the_name()
    {
        Assert.Equal(1, await Run(Set));
        Assert.Empty(_firewall.Rules);
    }

    [Fact]
    public async Task Set_answers_1_when_the_rule_has_the_other_direction()
    {
        await Run(Add.Replace("dir=in", "dir=out"));

        Assert.Equal(1, await Run(Set));
        Assert.Equal(@"C:\g\m.exe", Assert.Single(_firewall.Rules).Program);
    }

    [Theory]
    [InlineData("advfirewall firewall set rule name=\"R\" dir=in program=\"D:\\h\\m.exe\"")]
    [InlineData("advfirewall firewall set rule name=\"R\" dir=in new")]
    [InlineData("advfirewall firewall set rule name=\"R\" dir=in new action=allow")]
    public async Task Set_without_the_new_values_answers_1(string command)
    {
        await Run(Add);

        Assert.Equal(1, await Run(command));
        Assert.Equal(@"C:\g\m.exe", Assert.Single(_firewall.Rules).Program);
    }

    [Fact]
    public async Task Delete_is_not_a_command_the_fake_knows()
    {
        await Run(Add);

        Assert.Equal(1, await Run("advfirewall firewall delete rule name=\"R\""));
        Assert.Single(_firewall.Rules);
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
        Assert.Equal(1, await Run("advfirewall firewall nonsense"));
    }

    [Fact]
    public async Task Logs_each_call_with_the_runner_that_made_it()
    {
        await _firewall.Plain.RunAsync("netsh.exe", Show, default);
        await Run(Add);

        Assert.Equal(new[] { $"plain: netsh.exe {Show}", $"elevated: netsh.exe {Add}" }, _firewall.Log);
        Assert.Equal(new[] { $"netsh.exe {Show}" }, _firewall.PlainCalls);
        Assert.Equal(new[] { $"netsh.exe {Add}" }, _firewall.ElevatedCalls);
    }
}
