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
}
