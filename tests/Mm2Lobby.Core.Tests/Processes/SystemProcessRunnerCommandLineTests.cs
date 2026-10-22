using Mm2Lobby.Windows;

namespace Mm2Lobby.Core.Tests.Processes;

/// <summary>
/// The CreateProcess function of Windows does not run a batch file. So the
/// runner sends a batch file to cmd.exe. These tests cover the text that the
/// runner builds. They need no Windows, because the method is a pure
/// function of the two strings.
/// </summary>
public class SystemProcessRunnerCommandLineTests
{
    private const string Bat = @"C:\Program Files (x86)\ZeroTier\One\zerotier-cli.bat";

    [Fact]
    public void A_plain_program_keeps_its_name_and_its_arguments()
    {
        var line = SystemProcessRunner.BuildCommandLine("netsh.exe", "advfirewall show");

        Assert.Equal("netsh.exe", line.FileName);
        Assert.Equal("advfirewall show", line.Arguments);
    }

    [Fact]
    public void A_program_with_no_extension_keeps_its_name()
    {
        var line = SystemProcessRunner.BuildCommandLine("zerotier-cli", "-j info");

        Assert.Equal("zerotier-cli", line.FileName);
        Assert.Equal("-j info", line.Arguments);
    }

    [Fact]
    public void A_batch_file_runs_through_cmd_exe()
    {
        // The /s switch makes cmd.exe remove the first and the last quotation
        // mark only. The path holds a space and two brackets, so the path
        // needs its own pair of quotation marks.
        var line = SystemProcessRunner.BuildCommandLine(Bat, "-j info");

        Assert.Equal("cmd.exe", line.FileName);
        Assert.Equal($"/s /c \"\"{Bat}\" -j info\"", line.Arguments);
    }

    [Fact]
    public void A_batch_file_with_no_argument_runs_through_cmd_exe()
    {
        var line = SystemProcessRunner.BuildCommandLine(Bat, "");

        Assert.Equal("cmd.exe", line.FileName);
        Assert.Equal($"/s /c \"\"{Bat}\"\"", line.Arguments);
    }

    [Theory]
    [InlineData(@"C:\tools\run.cmd")]
    [InlineData(@"C:\tools\RUN.BAT")]
    [InlineData(@"C:\tools\Run.Cmd")]
    public void A_batch_file_of_either_kind_and_any_letter_case_runs_through_cmd_exe(string path)
    {
        Assert.Equal("cmd.exe", SystemProcessRunner.BuildCommandLine(path, "").FileName);
    }
}
