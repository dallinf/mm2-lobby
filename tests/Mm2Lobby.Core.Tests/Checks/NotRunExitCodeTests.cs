using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows;

namespace Mm2Lobby.Core.Tests.Checks;

/// <summary>
/// Three places use one fact: the real runner returns exit code 127 for a
/// program that did not run. These tests tie the places to one constant.
/// </summary>
public class NotRunExitCodeTests
{
    [Fact]
    public void The_constant_is_127()
    {
        Assert.Equal(127, SystemProcessRunner.NotRunExitCode);
    }

    [Fact]
    public async Task The_Core_fake_runner_answers_an_unknown_command_with_the_constant()
    {
        var result = await new FakeProcessRunner().RunAsync("unknown.exe", "", default);

        Assert.Equal(SystemProcessRunner.NotRunExitCode, result.ExitCode);
    }

    [Fact]
    public async Task The_elevated_fake_runner_answers_an_unknown_command_with_the_constant()
    {
        var result = await new FakeElevatedProcessRunner().RunAsync("unknown.exe", "", default);

        Assert.Equal(SystemProcessRunner.NotRunExitCode, result.ExitCode);
    }

    [Fact]
    public async Task The_firewall_fake_answers_another_program_with_the_constant()
    {
        var result = await new FakeFirewall().Plain.RunAsync("unknown.exe", "", default);

        Assert.Equal(SystemProcessRunner.NotRunExitCode, result.ExitCode);
    }
}
