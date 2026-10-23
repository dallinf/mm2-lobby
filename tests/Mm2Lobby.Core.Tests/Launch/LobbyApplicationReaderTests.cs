using Mm2Lobby.Core.Launch;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Launch;

public class LobbyApplicationReaderTests
{
    private const string Guid1 = "A1B2C3D4-0000-0000-0000-000000000001";

    private static LobbyApplicationKey Key(
        string name,
        string? guid = Guid1,
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
    public void The_reader_chooses_the_key_whose_name_holds_midtown()
    {
        var registration = LobbyApplicationReader.ReadFrom(
            [Key("Some Other Game"), Key("Midtown Madness 2")]);

        Assert.Equal(LobbyRegistrationFault.None, registration.Fault);
        Assert.Equal("Midtown Madness 2", registration.Entry!.KeyName);
    }

    [Fact]
    public void The_letter_case_of_the_key_name_does_not_matter()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key("MIDTOWN madness 2")]);

        Assert.Equal("MIDTOWN madness 2", registration.Entry!.KeyName);
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
            "Midtown Madness 2",
            commandLine: "-nointro",
            currentDirectory: @"C:\Games\MM2")]).Entry!;

        Assert.Equal(Guid.Parse(Guid1), entry.ApplicationGuid);
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
            ["GUID"] = Guid1,
            ["path"] = @"C:\Games\MM2",
            ["FILE"] = @"\midtown2.exe",
        };

        var entry = LobbyApplicationReader.ReadFrom(
            [new LobbyApplicationKey("Midtown Madness 2", values)]).Entry!;

        Assert.Equal(Guid.Parse(Guid1), entry.ApplicationGuid);
        Assert.Equal(@"C:\Games\MM2", entry.Folder);
        Assert.Equal(@"\midtown2.exe", entry.FileName);
    }

    [Fact]
    public void The_game_file_path_joins_the_folder_and_the_file_with_no_separator()
    {
        // DirectPlay joins Path and File with no separator, so File carries the
        // leading backslash. A run on Windows 11 proved this rule.
        var entry = LobbyApplicationReader.ReadFrom([Key("Midtown Madness 2")]).Entry!;

        Assert.Equal(@"C:\Games\MM2\midtown2.exe", entry.GameFilePath);
    }

    [Fact]
    public void A_file_value_with_no_leading_backslash_is_a_fault()
    {
        var registration = LobbyApplicationReader.ReadFrom(
            [Key("Midtown Madness 2", file: "midtown2.exe")]);

        Assert.Equal(LobbyRegistrationFault.FileWithoutLeadingBackslash, registration.Fault);
    }

    [Fact]
    public void A_Launcher_value_is_a_fault()
    {
        // A run on Windows 11 proved that a Launcher value stops the launch.
        var registration = LobbyApplicationReader.ReadFrom(
            [Key("Midtown Madness 2", launcher: @"C:\Games\MM2\launch.exe")]);

        Assert.Equal(LobbyRegistrationFault.LauncherValuePresent, registration.Fault);
    }

    [Fact]
    public void The_reader_still_returns_the_entry_beside_a_Launcher_value()
    {
        // The report names the key, so the owner can delete the one value.
        var registration = LobbyApplicationReader.ReadFrom(
            [Key("Midtown Madness 2", launcher: @"C:\Games\MM2\launch.exe")]);

        Assert.Equal("Midtown Madness 2", registration.Entry!.KeyName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a guid")]
    public void A_key_with_no_readable_guid_is_a_fault(string? guid)
    {
        var registration = LobbyApplicationReader.ReadFrom([Key("Midtown Madness 2", guid: guid)]);

        Assert.Equal(LobbyRegistrationFault.NoGuid, registration.Fault);
        Assert.Null(registration.Entry);
    }

    [Fact]
    public void A_key_with_no_path_is_a_fault()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key("Midtown Madness 2", path: null)]);

        Assert.Equal(LobbyRegistrationFault.NoPath, registration.Fault);
        Assert.Null(registration.Entry);
    }

    [Fact]
    public void A_key_with_no_file_is_a_fault()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key("Midtown Madness 2", file: null)]);

        Assert.Equal(LobbyRegistrationFault.NoFile, registration.Fault);
        Assert.Null(registration.Entry);
    }

    [Fact]
    public void A_blank_path_counts_as_no_path()
    {
        var registration = LobbyApplicationReader.ReadFrom([Key("Midtown Madness 2", path: "   ")]);

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
