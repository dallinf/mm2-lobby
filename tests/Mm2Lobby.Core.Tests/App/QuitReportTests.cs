using Mm2Lobby.App;

namespace Mm2Lobby.Core.Tests.App;

/// <summary>
/// These tests cover the text of the report. The builder is a pure function:
/// it reads the facts that the window holds, and it writes no file.
/// </summary>
public class QuitReportTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 10, 24, 19, 5, 11, TimeSpan.Zero);

    private static QuitReportFacts Facts() => new()
    {
        Time = Noon,
        AppVersion = "1.0.0.0",
        PlayerName = "dallin",
        NetworkId = "0123456789abcdef",
        Reach = "The app is on the network.",
        Title = "You host this race. Give the address to the others.",
    };

    [Fact]
    public void The_report_names_the_time_of_the_write()
    {
        Assert.Contains("Time: 2026-10-24 19:05:11 +00:00", QuitReport.Build(Facts()));
    }

    [Fact]
    public void The_report_names_the_version_the_player_and_the_network()
    {
        var text = QuitReport.Build(Facts());

        Assert.Contains("App version: 1.0.0.0", text);
        Assert.Contains("Player name: dallin", text);
        Assert.Contains("ZeroTier network ID: 0123456789abcdef", text);
    }

    [Theory]
    [InlineData(true, "yes")]
    [InlineData(false, "no")]
    public void The_report_says_whether_the_app_can_start_the_game_in_the_race(
        bool can, string word)
    {
        var text = QuitReport.Build(Facts() with { CanStartTheGameInTheRace = can });

        Assert.Contains($"The app can start the game in the race: {word}", text);
    }

    [Fact]
    public void The_report_names_the_network_the_title_and_the_address()
    {
        var text = QuitReport.Build(Facts() with { Address = "10.147.17.42" });

        Assert.Contains("The network: The app is on the network.", text);
        Assert.Contains("Title: You host this race. Give the address to the others.", text);
        Assert.Contains("Address: 10.147.17.42", text);
    }

    [Fact]
    public void The_report_says_that_the_window_showed_no_address()
    {
        var text = QuitReport.Build(Facts() with { Address = string.Empty });

        Assert.Contains("The window showed no address.", text);
    }

    [Fact]
    public void The_report_holds_one_line_for_every_setup_row()
    {
        var text = QuitReport.Build(Facts() with
        {
            Checks = new[]
            {
                new CheckRow("ZeroTier", "OK", "The computer holds an address.", string.Empty,
                    "Repair", false),
                new CheckRow("DirectPlay", "Problem", "Windows holds no DirectPlay.",
                    "The fix turns the feature on.", "Fix", true),
            },
        });

        Assert.Contains("ZeroTier | OK | The computer holds an address.", text);
        Assert.Contains(
            "DirectPlay | Problem | Windows holds no DirectPlay. | The fix turns the feature on.",
            text);
    }

    [Fact]
    public void The_report_says_that_the_app_ran_no_check()
    {
        Assert.Contains("The app ran no check.", QuitReport.Build(Facts()));
    }

    [Fact]
    public void The_report_holds_one_line_for_every_player()
    {
        var text = QuitReport.Build(Facts() with
        {
            Players = new[]
            {
                new PeerRow("chris", "In the lobby", "10.147.17.50", "Direct", false,
                    string.Empty),
            },
        });

        Assert.Contains("chris | In the lobby | 10.147.17.50 | Direct", text);
    }

    [Fact]
    public void The_report_names_the_version_of_a_player_that_differs()
    {
        var text = QuitReport.Build(Facts() with
        {
            Players = new[]
            {
                new PeerRow("chris", "Hosts a race", "10.147.17.50", "Relayed", true,
                    "Version 0.9.0.0 is different."),
            },
        });

        Assert.Contains(
            "chris | Hosts a race | 10.147.17.50 | Relayed | Version 0.9.0.0 is different.",
            text);
    }

    [Fact]
    public void The_report_says_that_the_list_held_no_player()
    {
        Assert.Contains("The list held no player.", QuitReport.Build(Facts()));
    }

    [Fact]
    public void The_report_holds_the_two_messages_of_the_window()
    {
        var text = QuitReport.Build(Facts() with
        {
            StatusMessage = "MM2 started. Follow the steps above.",
            FaultMessage = "The last refresh failed: the port is busy.",
        });

        Assert.Contains("Status: MM2 started. Follow the steps above.", text);
        Assert.Contains("Fault: The last refresh failed: the port is busy.", text);
    }

    [Fact]
    public void The_report_says_that_the_window_showed_no_message()
    {
        Assert.Contains("The window showed no message.", QuitReport.Build(Facts()));
    }

    [Fact]
    public void The_report_holds_the_trail_of_the_last_start()
    {
        var text = QuitReport.Build(Facts() with
        {
            LaunchReport = "CoInitializeEx answered 0x00000000.",
        });

        Assert.Contains("CoInitializeEx answered 0x00000000.", text);
    }

    [Fact]
    public void The_report_says_that_the_app_started_no_game_through_the_lobby()
    {
        Assert.Contains(
            "The app started no game through the lobby.", QuitReport.Build(Facts()));
    }

    [Fact]
    public void The_report_ends_every_line_the_way_Windows_does()
    {
        // The owner reads the file on Windows, and an editor of Windows can
        // show one long line for a file that holds the line end of Unix
        // alone. The trail below carries that line end, as a trail that this
        // computer built carries it.
        var text = QuitReport.Build(Facts() with
        {
            Checks = new[]
            {
                new CheckRow("ZeroTier", "OK", "The computer holds an address.", string.Empty,
                    "Repair", false),
            },
            LaunchReport = "the first call\nthe second call",
        });

        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty));
    }
}
