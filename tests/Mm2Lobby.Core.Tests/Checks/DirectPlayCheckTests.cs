using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

public class DirectPlayCheckTests
{
    private const string Library = @"C:\Windows\SysWOW64\dplayx.dll";

    private const string EnableArguments =
        "/online /enable-feature /featurename:DirectPlay /all /norestart";

    private readonly FakeElevatedProcessRunner _elevated = new();
    private readonly List<string> _probed = new();

    // The library exists when this flag is true. The flag models the disk.
    private bool _libraryExists;

    private DirectPlayCheck Check() => new(_elevated, path =>
    {
        _probed.Add(path);
        return _libraryExists && path == Library;
    });

    [Fact]
    public async Task Passes_when_the_SysWOW64_library_exists()
    {
        _libraryExists = true;

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Null(result.FixDescription);
        Assert.Equal(new[] { Library }, _probed);
    }

    [Fact]
    public async Task Fails_with_a_fix_that_names_the_component_when_the_library_is_missing()
    {
        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("DirectPlay", result.Message);
        Assert.Contains("DirectPlay Windows component", result.FixDescription);
    }

    [Fact]
    public async Task RunAsync_starts_no_process()
    {
        await Check().RunAsync(default);

        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task FixAsync_runs_DISM_through_the_elevated_runner_and_checks_the_library_again()
    {
        _elevated.Returns("dism.exe", EnableArguments, 0);
        var check = new DirectPlayCheck(_elevated, _ => _elevated.Calls.Count > 0);

        var result = await check.FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(new[] { $"dism.exe {EnableArguments}" }, _elevated.Calls);
    }

    [Fact]
    public async Task FixAsync_blames_the_permission_prompt_only_for_exit_code_127()
    {
        _elevated.Returns("dism.exe", EnableArguments, 127);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("exit code 127", result.Message);
        Assert.Contains("If you chose No at the permission prompt, that is the cause", result.Message);
        Assert.Contains($"dism {EnableArguments}", result.FixDescription);
        Assert.Contains("Press Fix again", result.FixDescription);
        Assert.Empty(_probed);
    }

    [Fact]
    public async Task FixAsync_reports_the_exit_code_and_says_the_command_failed_for_any_other_code()
    {
        _elevated.Returns("dism.exe", EnableArguments, 1);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("exit code 1", result.Message);
        Assert.Contains("The command failed", result.Message);
        Assert.DoesNotContain("permission prompt", result.Message);
        Assert.Contains($"dism {EnableArguments}", result.FixDescription);
        Assert.Empty(_probed);
    }

    [Fact]
    public async Task FixAsync_asks_for_a_restart_only_when_DISM_returns_3010_and_the_library_is_still_missing()
    {
        _elevated.Returns("dism.exe", EnableArguments, 3010);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("needs a restart", result.Message);
        Assert.Contains("Restart Windows", result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_does_not_ask_for_a_restart_when_DISM_returns_0_and_the_library_is_still_missing()
    {
        _elevated.Returns("dism.exe", EnableArguments, 0);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("reported success", result.Message);
        Assert.Contains(@"C:\Windows\SysWOW64\dplayx.dll", result.Message);
        Assert.DoesNotContain("estart", result.Message);
        Assert.DoesNotContain("estart", result.FixDescription);
        Assert.Contains("Report this", result.FixDescription);
    }

    [Fact]
    public async Task Tells_the_player_to_expect_one_permission_prompt()
    {
        var result = await Check().RunAsync(default);

        Assert.Contains("Windows will ask for permission one time", result.FixDescription);
        Assert.Contains("Choose Yes", result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_treats_exit_code_3010_as_success_when_the_library_exists()
    {
        _elevated.Returns("dism.exe", EnableArguments, 3010);
        var check = new DirectPlayCheck(_elevated, _ => true);

        var result = await check.FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Fact]
    public void Has_the_name_DirectPlay()
    {
        Assert.Equal("DirectPlay", Check().Name);
    }
}
