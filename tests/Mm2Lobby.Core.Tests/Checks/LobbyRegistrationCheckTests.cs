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
    private const string KeyName = "MM2 Lobby - Midtown Madness 2";
    private const string ScriptPath = @"C:\Temp\mm2-lobby.reg";

    /// <summary>The GUID that a run on two computers proved.</summary>
    private static readonly Guid TheGuid = LobbyRegistrationCheck.ApplicationGuid;

    private readonly AppConfig _config = new() { GamePath = GamePath };
    private readonly FakeElevatedProcessRunner _elevated = new();

    /// <summary>Every script that the check wrote, in order.</summary>
    private readonly List<string> _scripts = new();

    private LobbyRegistration _registration = Good();

    /// <summary>
    /// An entry as the reader gives it. The reader refuses any GUID but the
    /// one of the group, so an entry always carries that GUID.
    /// </summary>
    private static LobbyApplicationEntry Entry(
        string folder = Folder, string file = @"\midtown2.exe")
        => new(KeyName, TheGuid, folder, file, "", folder);

    private static LobbyRegistration Good()
        => new(Entry(), LobbyRegistrationFault.None);

    private static string Import(string path = ScriptPath) => $"reg.exe import \"{path}\"";

    private LobbyRegistrationCheck Check() => new(
        _config,
        _elevated,
        () => _registration,
        script =>
        {
            _scripts.Add(script);
            return ScriptPath;
        });

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
    public void The_app_writes_a_key_of_its_own_and_touches_no_other()
    {
        // An installer of the game writes a key of its own, and that key
        // holds the application GUID of the game. Another program such as
        // GameRanger reads that GUID. This app writes its own key, so it
        // breaks nothing that another program needs.
        Assert.Equal("MM2 Lobby - Midtown Madness 2", LobbyRegistrationCheck.KeyName);
    }

    [Fact]
    public void The_application_GUID_is_the_one_value_that_the_group_shares()
    {
        // DirectPlay shows a player only the sessions that carry the same
        // application GUID. A run on two computers on 2026-10-23 proved this
        // value, and every player already holds it.
        Assert.Equal(
            Guid.Parse("42418287-74D9-4C2D-A8D0-1B5D4D811C21"),
            LobbyRegistrationCheck.ApplicationGuid);
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
    public async Task Fails_when_the_entry_holds_another_application_GUID()
    {
        // A player whose key holds another GUID meets nobody of the group,
        // and MM2 reports no host and gives no reason. The reader reports the
        // fault, so the check and the launcher cannot disagree.
        var result = await ResultFor(LobbyRegistrationFault.WrongGuid);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            "The DirectPlay entry holds another application GUID, "
                + "so you would meet none of your friends.",
            result.Message);
    }

    [Fact]
    public async Task Fails_with_a_sentence_for_a_fault_that_the_check_does_not_name()
    {
        // A later fault of the reader must reach the player as a sentence. The
        // last arm of the switch reads the entry, and a fault that gives no
        // entry would otherwise throw there. The player would then read a
        // message about a null reference.
        var result = await ResultFor((LobbyRegistrationFault)999);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal("The DirectPlay entry is not complete.", result.Message);
    }

    [Fact]
    public async Task Fails_when_the_entry_names_another_game_file()
    {
        _registration = new LobbyRegistration(
            Entry(folder: @"D:\Old\MM2"), LobbyRegistrationFault.None);

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
            Entry(), LobbyRegistrationFault.LauncherValuePresent);

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
        _registration = new LobbyRegistration(null, fault);

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
            LobbyRegistrationFault.FileWithoutLeadingBackslash);

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
            Entry(), LobbyRegistrationFault.LauncherValuePresent);

        var result = await Check().RunAsync(default);

        Assert.Contains("The fix also deletes the Launcher value.", result.FixDescription);
    }

    [Fact]
    public async Task The_fix_runs_one_elevated_command_and_shows_one_prompt()
    {
        // Each elevated process shows one permission prompt. The fix writes
        // five values and deletes one, so it sends them all in one script.
        _registration = new LobbyRegistration(
            Entry(folder: @"D:\Old\MM2"), LobbyRegistrationFault.None);
        ImportWorks();

        await Check().FixAsync(default);

        Assert.Equal(Import(), Assert.Single(_elevated.Calls));
    }

    [Fact]
    public async Task The_fix_writes_every_value_of_the_entry()
    {
        _registration = new LobbyRegistration(
            Entry(folder: @"D:\Old\MM2"), LobbyRegistrationFault.None);
        ImportWorks();

        await Check().FixAsync(default);

        var script = Assert.Single(_scripts);

        Assert.Equal(
            "Windows Registry Editor Version 5.00\r\n"
                + "\r\n"
                + @"[HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\DirectPlay\Applications\MM2 Lobby - Midtown Madness 2]"
                + "\r\n"
                + "\"Guid\"=\"{42418287-74D9-4C2D-A8D0-1B5D4D811C21}\"\r\n"
                + "\"Path\"=\"C:\\\\Games\\\\Midtown Madness 2\"\r\n"
                + "\"File\"=\"\\\\midtown2.exe\"\r\n"
                + "\"CommandLine\"=\"\"\r\n"
                + "\"CurrentDirectory\"=\"C:\\\\Games\\\\Midtown Madness 2\"\r\n"
                + "\"Launcher\"=-\r\n"
                + "\r\n",
            script);
    }

    [Fact]
    public async Task The_fix_writes_the_key_of_this_app_and_names_no_other()
    {
        // An installer of the game writes its own key, and that key holds the
        // application GUID of the game. A fix that wrote over it would break
        // another program, and the player would never connect the two.
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.NoKey);
        ImportWorks();

        var script = Assert.Single(await Written());

        Assert.Contains(@"\Applications\MM2 Lobby - Midtown Madness 2]", script);
        Assert.DoesNotContain(@"\Applications\Midtown Madness 2]", script);
    }

    [Fact]
    public async Task The_fix_always_writes_the_GUID_of_the_group()
    {
        // The app owns this key, so the key holds the value of the group and
        // the app never generates a GUID.
        _registration = new LobbyRegistration(null, LobbyRegistrationFault.WrongGuid);
        ImportWorks();

        Assert.Contains(
            "\"Guid\"=\"{42418287-74D9-4C2D-A8D0-1B5D4D811C21}\"",
            Assert.Single(await Written()));
    }

    /// <summary>This method runs the fix and gives every script that it wrote.</summary>
    private async Task<List<string>> Written()
    {
        await Check().FixAsync(default);

        return _scripts;
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

        var result = await Check().FixAsync(default);

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

        var result = await Check().FixAsync(default);

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

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(
            "Windows accepted the command, and it still reports a problem with the entry.",
            result.Message);
    }

    [Fact]
    public async Task The_fix_reports_the_entry_that_it_wrote()
    {
        _registration = new LobbyRegistration(
            Entry(folder: @"D:\Old\MM2"), LobbyRegistrationFault.None);
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
            });

        var result = await check.FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(
            $"The app wrote the DirectPlay entry for Midtown Madness 2, and it names {GamePath}.",
            result.Message);
    }
}
