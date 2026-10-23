using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

/// <summary>
/// These tests drive the check over a fake of the Layers key. The real key
/// lives in HKCU, so the fix needs no administrator rights and no elevated
/// runner appears here.
/// </summary>
public class CompatibilityModeCheckTests
{
    private const string GamePath = @"C:\Games\Midtown Madness 2\midtown2.exe";

    private readonly AppConfig _config = new() { GamePath = GamePath };

    /// <summary>The value of the game under the Layers key. Null means no value.</summary>
    private string? _layers;

    /// <summary>Every write, in order. A null entry is a delete.</summary>
    private readonly List<string?> _writes = new();

    /// <summary>The path that the check read or wrote, each time.</summary>
    private readonly List<string> _paths = new();

    /// <summary>The exception that a write throws. A null value means no exception.</summary>
    private Exception? _writeFault;

    private CompatibilityModeCheck Check() => new(
        _config,
        path =>
        {
            _paths.Add(path);
            return _layers;
        },
        (path, value) =>
        {
            _paths.Add(path);

            if (_writeFault is not null) throw _writeFault;

            _writes.Add(value);
            _layers = value;
        });

    [Fact]
    public void The_check_is_advice_and_does_not_block_the_lobby()
    {
        // The app raced before this check existed. A player who leaves the
        // mode in place keeps that race.
        Assert.False(Check().Blocks);
    }

    [Fact]
    public void The_check_names_itself_for_the_row()
    {
        Assert.Equal("Compatibility mode", Check().Name);
    }

    [Fact]
    public async Task Passes_when_the_game_carries_no_layer_at_all()
    {
        _layers = null;

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal("Midtown Madness 2 runs with no legacy compatibility mode.", result.Message);
        Assert.Null(result.FixDescription);
    }

    [Fact]
    public async Task Passes_when_the_game_carries_a_layer_that_is_no_legacy_mode()
    {
        // The computer of the owner shows this token on other games. The
        // check must not call it a problem.
        _layers = "~ DWM8And16BitMitigation";

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Fact]
    public async Task The_check_reads_the_value_of_the_game_path()
    {
        _layers = null;

        await Check().RunAsync(default);

        Assert.Equal(GamePath, Assert.Single(_paths));
    }

    [Fact]
    public async Task Fails_and_names_the_mode_that_the_game_carries()
    {
        _layers = "~ WINXPSP2";

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            "Windows runs Midtown Madness 2 in the WINXPSP2 compatibility mode. "
                + "Windows then asks for administrator rights, and the one click "
                + "start cannot give them.",
            result.Message);
    }

    [Fact]
    public async Task Fails_and_names_both_modes_that_the_game_carries()
    {
        _layers = "~ WIN98 WINXPSP2 RUNASINVOKER";

        var result = await Check().RunAsync(default);

        Assert.Contains("the WIN98 and WINXPSP2 compatibility modes", result.Message);
    }

    [Fact]
    public async Task The_advice_warns_the_player_before_it_names_the_step()
    {
        // A warning comes before the step that it guards. The player must
        // know that the fix changes how the game runs, and must know it
        // before they press Fix.
        _layers = "~ WINXPSP2";

        var result = await Check().RunAsync(default);

        Assert.NotNull(result.FixDescription);
        Assert.StartsWith(
            "Warning: the fix changes how Windows runs Midtown Madness 2.",
            result.FixDescription);
    }

    [Fact]
    public async Task The_advice_says_what_the_fix_removes()
    {
        _layers = "~ WINXPSP2 RUNASINVOKER";

        var result = await Check().RunAsync(default);

        Assert.Contains(
            "The fix removes the WINXPSP2 compatibility mode and keeps every other setting",
            result.FixDescription);
    }

    [Fact]
    public async Task The_advice_says_that_Windows_asks_for_no_permission()
    {
        // This fix is the one fix of the app that needs no permission prompt.
        // A player who reads nothing would wait for a prompt that never comes.
        _layers = "~ WINXPSP2";

        var result = await Check().RunAsync(default);

        Assert.Contains("Windows asks for no permission", result.FixDescription);
    }

    [Fact]
    public async Task The_advice_says_that_the_race_goes_on_without_the_fix()
    {
        _layers = "~ WINXPSP2";

        var result = await Check().RunAsync(default);

        Assert.Contains("This check is advice", result.FixDescription);
        Assert.Contains("type the address of the host", result.FixDescription);
    }

    [Fact]
    public async Task Fails_when_the_app_does_not_know_the_game_path()
    {
        _config.GamePath = "";

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal("The app does not know where Midtown Madness 2 is.", result.Message);
        Assert.Equal("Run the Midtown Madness 2 check first.", result.FixDescription);
        Assert.Empty(_paths);
    }

    [Fact]
    public async Task The_fix_removes_the_mode_and_keeps_the_other_token()
    {
        _layers = "~ WINXPSP2 RUNASINVOKER";

        var result = await Check().FixAsync(default);

        Assert.Equal("~ RUNASINVOKER", Assert.Single(_writes));
        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Fact]
    public async Task The_fix_deletes_the_value_when_the_mode_was_the_only_token()
    {
        _layers = "~ WINXPSP2";

        await Check().FixAsync(default);

        Assert.Null(Assert.Single(_writes));
    }

    [Fact]
    public async Task The_fix_names_what_it_removed()
    {
        // The row shows the text of RunAsync after the fix, and that text
        // says only that the game is clean. Only this text names the change.
        _layers = "~ WINXPSP2";

        var result = await Check().FixAsync(default);

        Assert.Equal(
            "The app removed the WINXPSP2 compatibility mode from Midtown Madness 2.",
            result.Message);
    }

    [Fact]
    public async Task The_fix_writes_the_value_of_the_game_path()
    {
        _layers = "~ WINXPSP2";

        await Check().FixAsync(default);

        Assert.All(_paths, path => Assert.Equal(GamePath, path));
    }

    [Fact]
    public async Task The_fix_writes_nothing_when_the_game_carries_no_mode()
    {
        _layers = "~ DWM8And16BitMitigation";

        var result = await Check().FixAsync(default);

        Assert.Empty(_writes);
        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Fact]
    public async Task The_fix_reports_the_unknown_game_path_and_writes_nothing()
    {
        _config.GamePath = "";

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal("Run the Midtown Madness 2 check first.", result.FixDescription);
        Assert.Empty(_writes);
    }

    [Fact]
    public async Task The_fix_reports_a_write_that_Windows_refused()
    {
        // This fix writes HKCU and needs no administrator rights. A policy on
        // a work computer can still refuse the write, and the player must
        // read why.
        _layers = "~ WINXPSP2";
        _writeFault = new UnauthorizedAccessException("the key is read only");

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("the key is read only", result.Message);
        Assert.Contains("Compatibility", result.FixDescription);
    }

    [Fact]
    public async Task The_fix_reports_a_mode_that_the_recheck_still_finds()
    {
        // The write worked, and the value still holds the mode. The text must
        // differ from the text of RunAsync, which asks the player to do what
        // they have just done.
        _layers = "~ WINXPSP2";

        var check = new CompatibilityModeCheck(
            _config,
            _ => "~ WINXPSP2",
            (_, _) => _writes.Add(null));

        var result = await check.FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            "Windows accepted the change, and it still reports the compatibility mode.",
            result.Message);
    }
}
