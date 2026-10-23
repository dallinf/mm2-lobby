using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows;
using Mm2Lobby.Windows.Checks;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Checks;

/// <summary>
/// These tests drive the check over a fake of the registry read and a fake of
/// the elevated runner. The key is machine wide, so the fix runs one elevated
/// command and Windows shows one permission prompt.
/// </summary>
public class LobbyRegistrationCheckTests
{
    private const string Folder = @"C:\Games\Midtown Madness 2";
    private const string GamePath = $@"{Folder}\midtown2.exe";
    private const string KeyName = "Midtown Madness 2";
    private const string ScriptPath = @"C:\Temp\mm2-lobby.reg";

    private static readonly Guid TheGuid = new("A1B2C3D4-0000-0000-0000-000000000001");

    private readonly AppConfig _config = new() { GamePath = GamePath };
    private readonly FakeElevatedProcessRunner _elevated = new();

    /// <summary>Every script that the check wrote, in order.</summary>
    private readonly List<string> _scripts = new();

    private LobbyRegistration _registration = Good();

    private static LobbyApplicationEntry Entry(
        string folder = Folder, string file = @"\midtown2.exe")
        => new(KeyName, TheGuid, folder, file, "", folder);

    private static LobbyRegistration Good()
        => new(Entry(), LobbyRegistrationFault.None, KeyName);

    private static string Import(string path = ScriptPath) => $"reg.exe import \"{path}\"";

    private LobbyRegistrationCheck Check(Guid? sharedGuid = null) => new(
        _config,
        _elevated,
        () => _registration,
        script =>
        {
            _scripts.Add(script);
            return ScriptPath;
        },
        sharedGuid);

    /// <summary>The elevated runner answers with a success for the import.</summary>
    private void ImportWorks() => _elevated.Returns("reg.exe", $"import \"{ScriptPath}\"", 0);

    [Fact]
    public void The_check_is_advice_and_does_not_block_the_lobby()
    {
        // The app raced before this check existed. A player on a work
        // computer with no administrator rights keeps that race.
        Assert.False(Check().Blocks);
    }

    [Fact]
    public void The_check_names_itself_for_the_row()
    {
        Assert.Equal("DirectPlay registration", Check().Name);
    }

    [Fact]
    public async Task Passes_when_the_entry_names_the_game_of_this_computer()
    {
        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(
            "Windows knows how to start Midtown Madness 2 in a race.", result.Message);
        Assert.Null(result.FixDescription);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task The_letter_case_of_the_game_path_does_not_matter()
    {
        // Windows reads a path without the letter case, and the registry and
        // the file picker can give two cases of the same path.
        _config.GamePath = GamePath.ToUpperInvariant();

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
    }

    [Fact]
    public async Task Fails_when_no_key_registers_the_game()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            "Windows holds no DirectPlay entry for Midtown Madness 2. "
                + "The app cannot start the game in a race for you.",
            result.Message);
    }

    [Fact]
    public async Task Fails_when_the_entry_names_another_game_file()
    {
        _registration = new LobbyRegistration(
            Entry(folder: @"D:\Old\MM2"), LobbyRegistrationFault.None, KeyName);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            @"The DirectPlay entry names the file D:\Old\MM2\midtown2.exe. "
                + $"Your game is at {GamePath}.",
            result.Message);
    }

    [Fact]
    public async Task Fails_when_the_entry_holds_a_Launcher_value()
    {
        // A run on Windows 11 proved that this value stops the launch.
        _registration = new LobbyRegistration(
            Entry(), LobbyRegistrationFault.LauncherValuePresent, KeyName);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            "The DirectPlay entry holds a Launcher value, and that value stops the start.",
            result.Message);
    }

    /// <summary>
    /// The enum is internal to Mm2Lobby.Windows, so a public theory method
    /// cannot take it. Each case therefore gets its own fact.
    /// </summary>
    private async Task<CheckResult> ResultFor(LobbyRegistrationFault fault)
    {
        _registration = new LobbyRegistration(null, fault, KeyName);

        return await Check().RunAsync(default);
    }

    [Fact]
    public async Task Fails_when_the_entry_holds_no_GUID()
    {
        var result = await ResultFor(LobbyRegistrationFault.NoGuid);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal("The DirectPlay entry holds no GUID.", result.Message);
    }

    [Fact]
    public async Task Fails_when_the_entry_names_no_folder()
    {
        var result = await ResultFor(LobbyRegistrationFault.NoPath);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal("The DirectPlay entry names no folder.", result.Message);
    }

    [Fact]
    public async Task Fails_when_the_entry_names_no_file()
    {
        var result = await ResultFor(LobbyRegistrationFault.NoFile);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal("The DirectPlay entry names no file.", result.Message);
    }

    [Fact]
    public async Task Fails_when_the_File_value_starts_with_no_backslash()
    {
        // DirectPlay joins the folder and the file with no separator, so the
        // start looks for the wrong file.
        _registration = new LobbyRegistration(
            Entry(file: "midtown2.exe"),
            LobbyRegistrationFault.FileWithoutLeadingBackslash,
            KeyName);

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            "The File value of the DirectPlay entry starts with no backslash, "
                + "so Windows looks for the wrong file.",
            result.Message);
    }

    [Fact]
    public async Task Fails_when_the_app_does_not_know_the_game_path()
    {
        _config.GamePath = "";

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal("The app does not know where Midtown Madness 2 is.", result.Message);
        Assert.Equal("Run the Midtown Madness 2 check first.", result.FixDescription);
    }

    [Fact]
    public async Task The_advice_warns_the_player_before_it_names_the_step()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

        var result = await Check().RunAsync(default);

        Assert.NotNull(result.FixDescription);
        Assert.StartsWith(
            "Warning: the fix writes a Windows setting that every account on this "
                + "computer shares.",
            result.FixDescription);
    }

    [Fact]
    public async Task The_advice_names_one_permission_prompt()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

        var result = await Check().RunAsync(default);

        Assert.Contains("Windows asks for permission one time", result.FixDescription);
    }

    [Fact]
    public async Task The_advice_says_that_the_race_goes_on_without_the_fix()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

        var result = await Check().RunAsync(default);

        Assert.Contains("This check is advice", result.FixDescription);
        Assert.Contains("type the address of the host", result.FixDescription);
    }

    [Fact]
    public async Task The_advice_of_a_Launcher_value_says_that_the_fix_deletes_it()
    {
        _registration = new LobbyRegistration(
            Entry(), LobbyRegistrationFault.LauncherValuePresent, KeyName);

        var result = await Check().RunAsync(default);

        Assert.Contains("The fix also deletes the Launcher value.", result.FixDescription);
    }

    [Fact]
    public async Task The_fix_runs_one_elevated_command_and_shows_one_prompt()
    {
        // Each elevated process shows one permission prompt. The fix writes
        // five values and deletes one, so it sends them all in one script.
        _registration = new LobbyRegistration(
            Entry(folder: @"D:\Old\MM2"), LobbyRegistrationFault.None, KeyName);
        ImportWorks();

        await Check().FixAsync(default);

        Assert.Equal(Import(), Assert.Single(_elevated.Calls));
    }

    [Fact]
    public async Task The_fix_writes_every_value_of_the_entry()
    {
        _registration = new LobbyRegistration(
            Entry(folder: @"D:\Old\MM2"), LobbyRegistrationFault.None, KeyName);
        ImportWorks();

        await Check().FixAsync(default);

        var script = Assert.Single(_scripts);

        Assert.Equal(
            "Windows Registry Editor Version 5.00\r\n"
                + "\r\n"
                + @"[HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\DirectPlay\Applications\Midtown Madness 2]"
                + "\r\n"
                + "\"Guid\"=\"{A1B2C3D4-0000-0000-0000-000000000001}\"\r\n"
                + "\"Path\"=\"C:\\\\Games\\\\Midtown Madness 2\"\r\n"
                + "\"File\"=\"\\\\midtown2.exe\"\r\n"
                + "\"CommandLine\"=\"\"\r\n"
                + "\"CurrentDirectory\"=\"C:\\\\Games\\\\Midtown Madness 2\"\r\n"
                + "\"Launcher\"=-\r\n"
                + "\r\n",
            script);
    }

    [Fact]
    public async Task The_fix_writes_the_key_that_the_player_already_has()
    {
        // A second key that holds the word midtown would leave the reader a
        // choice between two keys.
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoGuid, "MM2 midtown");
        ImportWorks();

        await Check(TheGuid).FixAsync(default);

        Assert.Contains(@"\Applications\MM2 midtown]", Assert.Single(_scripts));
    }

    [Fact]
    public async Task The_fix_names_the_key_Midtown_Madness_2_when_no_key_exists()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);
        ImportWorks();

        await Check(TheGuid).FixAsync(default);

        Assert.Contains(@"\Applications\Midtown Madness 2]", Assert.Single(_scripts));
    }

    [Fact]
    public async Task The_fix_keeps_the_GUID_that_the_entry_already_holds()
    {
        // DirectPlay joins only players who share one application GUID. A
        // computer that already works holds the GUID of the game, and the fix
        // must not write another one over it.
        _registration = new LobbyRegistration(
            Entry(folder: @"D:\Old\MM2"), LobbyRegistrationFault.None, KeyName);
        ImportWorks();

        await Check(sharedGuid: Guid.Parse("FFFFFFFF-0000-0000-0000-000000000002"))
            .FixAsync(default);

        Assert.Contains("\"Guid\"=\"{A1B2C3D4-0000-0000-0000-000000000001}\"", _scripts[0]);
    }

    [Fact]
    public async Task The_fix_takes_the_shared_GUID_when_the_entry_holds_none()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);
        ImportWorks();

        await Check(sharedGuid: Guid.Parse("FFFFFFFF-0000-0000-0000-000000000002"))
            .FixAsync(default);

        Assert.Contains("\"Guid\"=\"{FFFFFFFF-0000-0000-0000-000000000002}\"", _scripts[0]);
    }

    [Fact]
    public async Task The_fix_refuses_to_invent_an_application_GUID()
    {
        // The app never generates a GUID. DirectPlay joins only players who
        // share one value, and a value that this computer invented would
        // match no friend. The player then reads one step that they can take.
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

        var result = await Check(sharedGuid: null).FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            "The app does not know the application GUID of Midtown Madness 2, "
                + "and it never invents one.",
            result.Message);
        Assert.Contains("the person who runs the group", result.FixDescription);
        Assert.Empty(_scripts);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task The_fix_reports_the_unknown_game_path_and_writes_nothing()
    {
        _config.GamePath = "";

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal("Run the Midtown Madness 2 check first.", result.FixDescription);
        Assert.Empty(_scripts);
        Assert.Empty(_elevated.Calls);
    }

    [Fact]
    public async Task The_fix_reports_a_game_path_that_holds_no_folder()
    {
        // The check runs on Windows, and a Windows path holds a backslash.
        // Path.GetDirectoryName reads no backslash on macOS, so the check
        // splits the path itself.
        _config.GamePath = "midtown2.exe";

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            "The game path midtown2.exe names no folder, so the entry cannot name one.",
            result.Message);
        Assert.Empty(_scripts);
    }

    [Fact]
    public async Task The_fix_reports_a_command_that_Windows_did_not_run()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);
        _elevated.Returns("reg.exe", $"import \"{ScriptPath}\"", SystemProcessRunner.NotRunExitCode);

        var result = await Check(TheGuid).FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("reg did not write the DirectPlay entry", result.Message);
        Assert.Contains("you chose No at the permission prompt", result.Message);
        Assert.Contains("reg import", result.FixDescription);
    }

    [Fact]
    public async Task The_fix_reports_a_command_that_failed()
    {
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);
        _elevated.Returns("reg.exe", $"import \"{ScriptPath}\"", 1);

        var result = await Check(TheGuid).FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("The command failed.", result.Message);
    }

    [Fact]
    public async Task The_fix_reports_the_entry_that_the_recheck_still_refuses()
    {
        // The command worked, and the registry still reports a fault. This
        // text must differ from the text of RunAsync, which asks the player
        // to do what they have just done.
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);
        ImportWorks();

        var result = await Check(TheGuid).FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            "Windows accepted the command, and it still reports a problem with the entry.",
            result.Message);
    }

    [Fact]
    public async Task The_fix_reports_the_entry_that_it_wrote()
    {
        _registration = new LobbyRegistration(
            Entry(folder: @"D:\Old\MM2"), LobbyRegistrationFault.None, KeyName);
        ImportWorks();

        // The registry answers with the mended entry after the import.
        var check = new LobbyRegistrationCheck(
            _config,
            _elevated,
            () => _registration,
            script =>
            {
                _scripts.Add(script);
                _registration = Good();
                return ScriptPath;
            },
            null);

        var result = await check.FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(
            $"The app wrote the DirectPlay entry for Midtown Madness 2, and it names {GamePath}.",
            result.Message);
    }
}
