using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Core.Tests.Processes;

public class ProcessResultTests
{
    [Fact]
    public void Succeeded_is_true_for_exit_code_zero()
    {
        var result = new ProcessResult(0, "output", "");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Succeeded_is_false_for_any_other_exit_code()
    {
        var result = new ProcessResult(1, "", "boom");
        Assert.False(result.Succeeded);
    }
}
