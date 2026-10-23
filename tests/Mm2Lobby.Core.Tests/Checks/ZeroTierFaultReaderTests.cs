using Mm2Lobby.Core.Processes;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

/// <summary>
/// ZeroTierClient answers null for every failure, by its design. These tests
/// cover the class that tells one failure from another, because each failure
/// has a different fix.
/// </summary>
public class ZeroTierFaultReaderTests
{
    private const string Bat = @"C:\Program Files (x86)\ZeroTier\One\zerotier-cli.bat";

    /// <summary>The text that zerotier-cli prints when it cannot read the token.</summary>
    private const string TokenText =
        "authtoken.secret not found or readable in C:\\ProgramData\\ZeroTier\\One "
        + "(try again as root)";

    private readonly FakeProcessRunner _runner = new();

    private ZeroTierFaultReader Reader()
        => new(_runner, new ZeroTierCommand(Bat, new[] { "a folder" }));

    private void Answers(int exitCode, string output = "", string error = "")
        => _runner.Returns(Bat, "-j info", new ProcessResult(exitCode, output, error));

    [Fact]
    public async Task Reads_CommandNotFound_when_the_command_did_not_run()
    {
        // The fake runner answers NotRunExitCode for a program it does not
        // know, as the real runner does for a program that is not there.
        Assert.Equal(ZeroTierFault.CommandNotFound, await Reader().ReadAsync(default));
    }

    [Fact]
    public async Task Reads_PermissionDenied_when_ZeroTier_cannot_read_its_token()
    {
        Answers(ZeroTierFaultReader.TokenExitCode, output: TokenText);

        Assert.Equal(ZeroTierFault.PermissionDenied, await Reader().ReadAsync(default));
    }

    [Fact]
    public async Task Reads_PermissionDenied_when_the_text_arrives_on_the_error_stream()
    {
        // The app cannot know which of the two streams carries the text. So
        // the reader looks at both.
        Answers(ZeroTierFaultReader.TokenExitCode, error: TokenText);

        Assert.Equal(ZeroTierFault.PermissionDenied, await Reader().ReadAsync(default));
    }

    [Fact]
    public async Task Reads_Unknown_when_exit_code_two_names_no_token_file()
    {
        Answers(ZeroTierFaultReader.TokenExitCode, output: "something else failed");

        Assert.Equal(ZeroTierFault.Unknown, await Reader().ReadAsync(default));
    }

    [Fact]
    public async Task Reads_Unknown_when_another_exit_code_names_the_token_file()
    {
        Answers(1, output: TokenText);

        Assert.Equal(ZeroTierFault.Unknown, await Reader().ReadAsync(default));
    }

    [Fact]
    public async Task Reads_Unknown_when_the_command_answers_without_a_fault()
    {
        // The command ran and gave an answer that the client could not read.
        Answers(0, output: "not json at all");

        Assert.Equal(ZeroTierFault.Unknown, await Reader().ReadAsync(default));
    }

    [Fact]
    public async Task Runs_the_command_file_that_the_locator_found()
    {
        Answers(0);

        await Reader().ReadAsync(default);

        Assert.Equal(new[] { $"{Bat} -j info" }, _runner.Calls);
    }

    [Fact]
    public void The_token_exit_code_is_two()
    {
        Assert.Equal(2, ZeroTierFaultReader.TokenExitCode);
    }

    [Fact]
    public void The_reader_keeps_the_command_that_the_locator_found()
    {
        var command = new ZeroTierCommand("zerotier-cli", new[] { "one", "two" });

        Assert.Same(command, new ZeroTierFaultReader(_runner, command).Command);
    }

    [Fact]
    public async Task A_missing_command_and_a_stopped_service_are_two_faults()
    {
        // This is the defect of the real Windows run. The app reported a name
        // that does not resolve as a stopped service. The two faults must
        // not share one value.
        var missing = await Reader().ReadAsync(default);

        Answers(SystemProcessRunner.NotRunExitCode - 1, error: "the service is not running");
        var stopped = await Reader().ReadAsync(default);

        Assert.Equal(ZeroTierFault.CommandNotFound, missing);
        Assert.Equal(ZeroTierFault.Unknown, stopped);
        Assert.NotEqual(missing, stopped);
    }
}
