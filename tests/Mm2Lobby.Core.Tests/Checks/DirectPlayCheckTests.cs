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
    public async Task Fails_with_an_administrator_fix_when_the_library_is_missing()
    {
        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("DirectPlay", result.Message);
        Assert.Contains("administrator", result.FixDescription);
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

    [Theory]
    [InlineData(1)]
    [InlineData(127)]
    public async Task FixAsync_reports_the_exit_code_and_the_manual_command_when_DISM_fails(
        int exitCode)
    {
        _elevated.Returns("dism.exe", EnableArguments, exitCode);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains($"exit code {exitCode}", result.Message);
        Assert.Contains("administrator prompt", result.Message);
        Assert.Contains($"dism {EnableArguments}", result.FixDescription);
        Assert.Empty(_probed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3010)]
    public async Task FixAsync_asks_for_a_restart_when_DISM_succeeds_but_the_library_is_still_missing(
        int exitCode)
    {
        _elevated.Returns("dism.exe", EnableArguments, exitCode);

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("restart", result.FixDescription, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exit code", result.Message);
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
