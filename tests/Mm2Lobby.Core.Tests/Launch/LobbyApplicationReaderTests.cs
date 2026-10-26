using Mm2Lobby.Core.Launch;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Launch;

public class LobbyApplicationReaderTests
{
    /// <summary>The GUID of the group, as the key of this app holds it.</summary>
    private const string OurGuid = "42418287-74D9-4C2D-A8D0-1B5D4D811C21";

    /// <summary>The GUID of the game, as the key of an installer holds it.</summary>
    private const string AnotherGuid = "A1B2C3D4-0000-0000-0000-000000000002";

    private const string OurKey = "MM2 Lobby - Midtown Madness 2";

    private static LobbyApplicationKey Key(
        string name = OurKey,
        string? guid = OurGuid,
        string? path = @"C:\Games\MM2",
        string? file = @"\midtown2.exe",
        string? commandLine = "",
        string? currentDirectory = @"C:\Games\MM2",
        string? launcher = null)
    {
        var values = new Dictionary<string, string>();
        if (guid is not null) values["Guid"] = guid;
        if (path is not null) values["Path"] = path;
        if (file is not null) values["File"] = file;
        if (commandLine is not null) values["CommandLine"] = commandLine;
        if (currentDirectory is not null) values["CurrentDirectory"] = currentDirectory;
        if (launcher is not null) values["Launcher"] = launcher;

        return new LobbyApplicationKey(name, values);
    }

    [Fact]
    public void A_registry_with_no_key_reports_that_nothing_registered_the_game()
    {
        var registration = LobbyApplicationReader.ReadFrom([]);

        Assert.Equal(LobbyRegistrationFault.NoKey, registration.Fault);
        Assert.Null(registration.Entry);
    }

    [Fact]
    public void The_name_of_the_key_of_this_app_is_the_name_the_group_documents()
    {
        // The firewall rules of the project carry the same name. The app owns
        // every thing with that name, and it changes nothing else.
        Assert.Equal(OurKey, LobbyApplicationReader.OwnKeyName);
    }

    [Fact]
    public void The_GUID_of_the_group_is_the_one_value_that_every_player_holds()
    {
        // A run on two computers on 2026-10-23 proved this value. DirectPlay
        // shows a player only the sessions that carry the same application
        // GUID.
        Assert.Equal(Guid.Parse(OurGuid), LobbyApplicationReader.ApplicationGuid);
    }

    [Fact]
    public void The_reader_reads_the_key_of_this_app()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key()]);

        Assert.Equal(LobbyRegistrationFault.None, registration.Fault);
        Assert.Equal(OurKey, registration.Entry!.KeyName);
    }

    [Fact]
    public void The_reader_reads_no_key_of_another_name()
    {
        // WARNING: this rule stops the worst failure of the project. The key
        // of an installer holds the application GUID of the game, and the
        // group shares another GUID. A launch against that key opens a
        // session that looks right and that no friend can see. MM2 reports
        // nothing, so nobody can report it.
        var registration = LobbyApplicationReader.ReadFrom(
            [Key("Midtown Madness 2", guid: AnotherGuid)]);

        Assert.Equal(LobbyRegistrationFault.NoKey, registration.Fault);
        Assert.Null(registration.Entry);
    }

    [Fact]
    public void The_reader_passes_over_another_key_and_reads_ours()
    {
        // The name of our key also holds the word midtown, so a match on that
        // word could read either key.
        var registration = LobbyApplicationReader.ReadFrom(
            [Key("Midtown Madness 2", guid: AnotherGuid), Key()]);

        Assert.Equal(OurKey, registration.Entry!.KeyName);
        Assert.Equal(Guid.Parse(OurGuid), registration.Entry.ApplicationGuid);
    }

    [Fact]
    public void A_key_that_holds_the_word_midtown_is_not_the_key_of_this_app()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key("Some Midtown Game")]);

        Assert.Equal(LobbyRegistrationFault.NoKey, registration.Fault);
    }

    [Fact]
    public void A_key_with_another_application_GUID_is_a_fault()
    {
        // The app owns this key, so the key holds the GUID of the group. A
        // player whose key holds another value meets nobody. The reader
        // reports it, so the setup check and the launcher cannot disagree.
        var registration = LobbyApplicationReader.ReadFrom([Key(guid: AnotherGuid)]);

        Assert.Equal(LobbyRegistrationFault.WrongGuid, registration.Fault);
        Assert.Null(registration.Entry);
    }

    [Fact]
    public void The_letter_case_of_the_application_GUID_does_not_matter()
    {
        var registration = LobbyApplicationReader.ReadFrom(
            [Key(guid: OurGuid.ToLowerInvariant())]);

        Assert.Equal(LobbyRegistrationFault.None, registration.Fault);
    }

    [Fact]
    public void The_braces_of_the_application_GUID_do_not_matter()
    {
        // A registry script writes the GUID inside braces, and the registry
        // keeps them.
        var registration = LobbyApplicationReader.ReadFrom([Key(guid: $"{{{OurGuid}}}")]);

        Assert.Equal(LobbyRegistrationFault.None, registration.Fault);
    }

    [Fact]
    public void The_letter_case_of_the_key_name_does_not_matter()
    {
        // The registry ignores the letter case of a key name.
        var registration = LobbyApplicationReader.ReadFrom([Key("mm2 LOBBY - midtown madness 2")]);

        Assert.Equal(LobbyRegistrationFault.None, registration.Fault);
    }

    [Fact]
    public void A_key_that_does_not_name_the_game_does_not_count()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key("Some Other Game")]);

        Assert.Equal(LobbyRegistrationFault.NoKey, registration.Fault);
        Assert.Null(registration.Entry);
    }

    [Fact]
    public void The_reader_reads_every_value_of_the_key()
    {
        var entry = LobbyApplicationReader.ReadFrom([Key(
            commandLine: "-nointro",
            currentDirectory: @"C:\Games\MM2")]).Entry!;

        Assert.Equal(Guid.Parse(OurGuid), entry.ApplicationGuid);
        Assert.Equal(@"C:\Games\MM2", entry.Folder);
        Assert.Equal(@"\midtown2.exe", entry.FileName);
        Assert.Equal("-nointro", entry.CommandLine);
        Assert.Equal(@"C:\Games\MM2", entry.CurrentDirectory);
    }

    [Fact]
    public void The_letter_case_of_a_value_name_does_not_matter()
    {
        var values = new Dictionary<string, string>
        {
            ["GUID"] = OurGuid,
            ["path"] = @"C:\Games\MM2",
            ["FILE"] = @"\midtown2.exe",
        };

        var entry = LobbyApplicationReader.ReadFrom(
            [new LobbyApplicationKey(OurKey, values)]).Entry!;

        Assert.Equal(Guid.Parse(OurGuid), entry.ApplicationGuid);
        Assert.Equal(@"C:\Games\MM2", entry.Folder);
        Assert.Equal(@"\midtown2.exe", entry.FileName);
    }

    [Fact]
    public void The_game_file_path_joins_the_folder_and_the_file_with_no_separator()
    {
        // DirectPlay joins Path and File with no separator, so File carries the
        // leading backslash. A run on Windows 11 proved this rule.
        var entry = LobbyApplicationReader.ReadFrom([Key()]).Entry!;

        Assert.Equal(@"C:\Games\MM2\midtown2.exe", entry.GameFilePath);
    }

    [Fact]
    public void A_file_value_with_no_leading_backslash_is_a_fault()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key(file: "midtown2.exe")]);

        Assert.Equal(LobbyRegistrationFault.FileWithoutLeadingBackslash, registration.Fault);
    }

    [Fact]
    public void A_Launcher_value_is_a_fault()
    {
        // A run on Windows 11 proved that a Launcher value stops the launch.
        var registration = LobbyApplicationReader.ReadFrom(
            [Key(launcher: @"C:\Games\MM2\launch.exe")]);

        Assert.Equal(LobbyRegistrationFault.LauncherValuePresent, registration.Fault);
    }

    [Fact]
    public void The_reader_still_returns_the_entry_beside_a_Launcher_value()
    {
        // The report names the key, so the owner can delete the one value.
        var registration = LobbyApplicationReader.ReadFrom(
            [Key(launcher: @"C:\Games\MM2\launch.exe")]);

        Assert.Equal(OurKey, registration.Entry!.KeyName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a guid")]
    public void A_key_with_no_readable_guid_is_a_fault(string? guid)
    {
        var registration = LobbyApplicationReader.ReadFrom([Key(guid: guid)]);

        Assert.Equal(LobbyRegistrationFault.NoGuid, registration.Fault);
        Assert.Null(registration.Entry);
    }

    [Fact]
    public void A_key_with_no_path_is_a_fault()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key(path: null)]);

        Assert.Equal(LobbyRegistrationFault.NoPath, registration.Fault);
        Assert.Null(registration.Entry);
    }

    [Fact]
    public void A_key_with_no_file_is_a_fault()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key(file: null)]);

        Assert.Equal(LobbyRegistrationFault.NoFile, registration.Fault);
        Assert.Null(registration.Entry);
    }

    [Fact]
    public void A_blank_path_counts_as_no_path()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key(path: "   ")]);

        Assert.Equal(LobbyRegistrationFault.NoPath, registration.Fault);
    }

    [Fact]
    public void A_registry_with_no_key_asks_the_player_to_register_the_game()
    {
        Assert.Equal(
            LobbyLaunchFault.GameNotRegistered,
            DirectPlayLobbyLauncher.FaultOf(LobbyRegistrationFault.NoKey));
    }

    [Fact]
    public void A_key_with_a_wrong_value_asks_the_player_to_mend_the_registration()
    {
        // A missing key and a wrong value need different steps from the player.
        var wrong = Enum.GetValues<LobbyRegistrationFault>()
            .Where(one => one is not (LobbyRegistrationFault.None or LobbyRegistrationFault.NoKey))
            .ToList();

        Assert.NotEmpty(wrong);

        foreach (var fault in wrong)
        {
            Assert.Equal(
                LobbyLaunchFault.GameRegistrationWrong,
                DirectPlayLobbyLauncher.FaultOf(fault));
        }
    }
}
