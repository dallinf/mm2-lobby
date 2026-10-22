using Mm2Lobby.Core.Processes;
using Mm2Lobby.Core.Tests.Fakes;

namespace Mm2Lobby.Core.Tests.Checks;

public class FakeElevatedProcessRunnerTests
{
    [Fact]
    public async Task Returns_the_exit_code_and_never_any_output()
    {
        var runner = new FakeElevatedProcessRunner().Returns("netsh.exe", "a b", 0);

        var result = await runner.RunAsync("netsh.exe", "a b", default);

        Assert.Equal(new ProcessResult(0, "", ""), result);
    }

    [Fact]
    public async Task Answers_an_unknown_command_with_exit_code_127()
    {
        var runner = new FakeElevatedProcessRunner();

        var result = await runner.RunAsync("netsh.exe", "unexpected", default);

        Assert.Equal(127, result.ExitCode);
    }

    [Fact]
    public async Task Throws_for_an_empty_file_name_as_Process_Start_does()
    {
        var runner = new FakeElevatedProcessRunner();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("", "a b", default));
        Assert.Empty(runner.Calls);
    }
}
