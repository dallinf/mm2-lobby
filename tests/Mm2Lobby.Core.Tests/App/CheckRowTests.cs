using Mm2Lobby.App;
using Mm2Lobby.Core.Preflight;

namespace Mm2Lobby.Core.Tests.App;

public class CheckRowTests
{
    private const string Advice = "Start the ZeroTier One service in Windows Services.";

    private static CheckResult Result(CheckStatus status)
        => new("ZeroTier", status, "The app cannot reach ZeroTier.", Advice);

    [Fact]
    public void From_shows_the_whole_message_and_the_whole_advice()
    {
        // The message of the ZeroTier check names the one dead end that a
        // player cannot work out alone. The window must not cut it.
        var row = CheckRow.From(Result(CheckStatus.Fail), null);

        Assert.Equal("The app cannot reach ZeroTier.", row.Message);
        Assert.Equal(Advice, row.Advice);
    }

    [Fact]
    public void From_offers_Fix_for_a_check_that_fails()
    {
        var row = CheckRow.From(Result(CheckStatus.Fail), null);

        Assert.True(row.ShowAction);
        Assert.Equal("Fix", row.ActionLabel);
    }

    [Fact]
    public void From_offers_Fix_for_a_check_that_could_not_run()
    {
        var row = CheckRow.From(Result(CheckStatus.Unavailable), null);

        Assert.True(row.ShowAction);
        Assert.Equal("Fix", row.ActionLabel);
    }

    [Fact]
    public void From_offers_Repair_for_a_check_that_passes_with_a_hint()
    {
        // The firewall check cannot see that a rule names an old game path.
        // Then the check passes and the firewall still blocks the game.
        var row = CheckRow.From(
            new CheckResult("Windows Firewall", CheckStatus.Pass, "Both rules are in place.", null),
            "The rule can name an old game folder.");

        Assert.True(row.ShowAction);
        Assert.Equal("Repair", row.ActionLabel);
        Assert.Equal("The rule can name an old game folder.", row.Advice);
    }

    [Fact]
    public void From_offers_no_action_for_a_check_that_passes_with_no_hint()
    {
        var row = CheckRow.From(Result(CheckStatus.Pass), null);

        Assert.False(row.ShowAction);
    }

    [Theory]
    [InlineData(CheckStatus.Pass, "OK")]
    [InlineData(CheckStatus.Fail, "Problem")]
    [InlineData(CheckStatus.Unavailable, "Unknown")]
    public void From_shows_the_state_of_the_check(CheckStatus status, string expected)
    {
        var row = CheckRow.From(Result(status), null);

        Assert.Equal(expected, row.Mark);
    }

    [Fact]
    public void From_shows_an_empty_advice_when_the_check_gives_none()
    {
        var row = CheckRow.From(
            new CheckResult("DirectPlay", CheckStatus.Fail, "DirectPlay is off.", null), null);

        Assert.Equal(string.Empty, row.Advice);
    }

    private static CheckResult Advisory(CheckStatus status)
        => new("Compatibility mode", status, "The game runs in WINXPSP2.", "Press Fix.")
        {
            Blocks = false,
        };

    [Theory]
    [InlineData(CheckStatus.Fail)]
    [InlineData(CheckStatus.Unavailable)]
    public void From_marks_an_advisory_row_that_does_not_pass_as_optional(CheckStatus status)
    {
        // The row must not read like a blocking row. A player who reads
        // Problem beside this row believes that the race stopped, and the
        // race did not stop.
        var row = CheckRow.From(Advisory(status), null);

        Assert.Equal("Optional", row.Mark);
    }

    [Fact]
    public void From_marks_an_advisory_row_that_passes_as_OK()
    {
        var row = CheckRow.From(Advisory(CheckStatus.Pass), null);

        Assert.Equal("OK", row.Mark);
    }

    [Fact]
    public void From_still_offers_Fix_on_an_advisory_row()
    {
        // The window shows the row, the reason and the button. Only the gate
        // on Host and Join ignores this check.
        var row = CheckRow.From(Advisory(CheckStatus.Fail), null);

        Assert.True(row.ShowAction);
        Assert.Equal("Fix", row.ActionLabel);
        Assert.Equal("The game runs in WINXPSP2.", row.Message);
        Assert.Equal("Press Fix.", row.Advice);
    }
}
