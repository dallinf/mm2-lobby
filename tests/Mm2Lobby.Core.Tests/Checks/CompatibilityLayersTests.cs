using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

/// <summary>
/// These tests cover the rule that decides what the fix removes. A player can
/// carry a layer that is not a legacy operating system mode, and the computer
/// of the owner shows DWM8And16BitMitigation on other games. The fix must keep
/// such a token.
/// </summary>
public class CompatibilityLayersTests
{
    [Fact]
    public void The_table_holds_the_modes_to_test()
    {
        // Without this test, every loop below would pass over an empty table.
        Assert.NotEmpty(CompatibilityLayers.ModesThatDemandElevation);
    }

    [Theory]
    [InlineData("WIN95")]
    [InlineData("WIN98")]
    [InlineData("WINXPSP2")]
    [InlineData("WINXPSP3")]
    [InlineData("VISTARTM")]
    [InlineData("VISTASP1")]
    [InlineData("VISTASP2")]
    [InlineData("WIN7RTM")]
    [InlineData("WIN8RTM")]
    public void The_table_names_every_known_operating_system_mode(string mode)
    {
        Assert.Contains(mode, CompatibilityLayers.ModesThatDemandElevation);
    }

    // A player sets this box by hand on an old game, and the computer of the
    // owner carries RUNASADMIN on another game. Windows then asks for
    // administrator rights, and the one press start cannot answer.
    [Theory]
    [InlineData("RUNASADMIN")]
    [InlineData("RUNASHIGHEST")]
    public void The_table_names_the_token_that_runs_the_game_as_an_administrator(string mode)
    {
        Assert.Contains(mode, CompatibilityLayers.ModesThatDemandElevation);
    }

    [Theory]
    [InlineData("RUNASADMIN")]
    [InlineData("RUNASHIGHEST")]
    public void ModesIn_reports_the_token_that_runs_the_game_as_an_administrator(string mode)
    {
        Assert.Equal(new[] { mode }, CompatibilityLayers.ModesIn($"~ {mode}"));
    }

    [Fact]
    public void Without_removes_the_token_that_runs_the_game_as_an_administrator()
    {
        Assert.Equal(
            "~ DWM8And16BitMitigation",
            CompatibilityLayers.Without("~ RUNASADMIN DWM8And16BitMitigation"));
        Assert.Equal(
            "~ DWM8And16BitMitigation",
            CompatibilityLayers.Without("~ RUNASHIGHEST DWM8And16BitMitigation"));
    }

    [Fact]
    public void Without_keeps_the_token_that_runs_the_game_as_the_player()
    {
        // RUNASINVOKER is the opposite of RUNASADMIN. It asks for no rights,
        // so the fix keeps it.
        Assert.Equal("~ RUNASINVOKER", CompatibilityLayers.Without("~ RUNASADMIN RUNASINVOKER"));
    }

    [Fact]
    public void A_value_with_no_mode_reports_no_mode()
    {
        Assert.Empty(CompatibilityLayers.ModesIn(null));
        Assert.Empty(CompatibilityLayers.ModesIn(""));
        Assert.Empty(CompatibilityLayers.ModesIn("~"));
        Assert.Empty(CompatibilityLayers.ModesIn("~ DWM8And16BitMitigation"));
    }

    [Fact]
    public void A_value_with_one_mode_reports_that_mode()
    {
        Assert.Equal(new[] { "WINXPSP2" }, CompatibilityLayers.ModesIn("~ WINXPSP2"));
    }

    [Fact]
    public void A_value_with_two_modes_reports_both_in_the_order_of_the_value()
    {
        Assert.Equal(
            new[] { "WIN98", "WINXPSP2" },
            CompatibilityLayers.ModesIn("~ WIN98 WINXPSP2"));
    }

    [Fact]
    public void The_letter_case_of_a_mode_does_not_matter()
    {
        // The registry holds these tokens in capital letters. A player who
        // wrote the value by hand can hold another case, and the mode still
        // works in Windows.
        Assert.Equal(new[] { "winxpsp2" }, CompatibilityLayers.ModesIn("~ winxpsp2"));
    }

    [Fact]
    public void Without_removes_the_mode_and_keeps_the_other_tokens()
    {
        // RUNASINVOKER is not an operating system mode, so the fix keeps it.
        Assert.Equal(
            "~ RUNASINVOKER",
            CompatibilityLayers.Without("~ WINXPSP2 RUNASINVOKER"));
    }

    [Fact]
    public void Without_keeps_a_token_that_the_table_does_not_name()
    {
        // The computer of the owner shows this token on other games.
        Assert.Equal(
            "~ DWM8And16BitMitigation",
            CompatibilityLayers.Without("~ WIN98 DWM8And16BitMitigation"));
    }

    [Fact]
    public void Without_removes_every_mode_of_a_value_that_holds_two()
    {
        Assert.Equal("~ RUNASINVOKER", CompatibilityLayers.Without("~ WIN98 WINXPSP2 RUNASINVOKER"));
    }

    [Fact]
    public void Without_gives_null_when_the_mode_was_the_only_token()
    {
        // A null answer tells the check to delete the whole value. A value of
        // "~" alone would leave an entry that means nothing.
        Assert.Null(CompatibilityLayers.Without("~ WINXPSP2"));
        Assert.Null(CompatibilityLayers.Without("WINXPSP2"));
    }

    [Fact]
    public void Without_gives_null_for_a_value_that_holds_nothing_to_keep()
    {
        Assert.Null(CompatibilityLayers.Without(null));
        Assert.Null(CompatibilityLayers.Without("~"));
    }

    [Fact]
    public void Without_keeps_a_value_that_holds_no_mode()
    {
        Assert.Equal(
            "~ DWM8And16BitMitigation",
            CompatibilityLayers.Without("~ DWM8And16BitMitigation"));
    }

    [Fact]
    public void Without_writes_one_space_between_the_tokens_that_it_keeps()
    {
        Assert.Equal(
            "~ RUNASINVOKER HIGHDPIAWARE",
            CompatibilityLayers.Without("~   WIN98   RUNASINVOKER   HIGHDPIAWARE"));
    }

    [Fact]
    public void Without_keeps_the_first_token_of_a_value_that_starts_with_no_tilde()
    {
        // Windows writes the tilde, and the fix must not invent one or drop
        // one. The fix changes the modes and nothing else.
        Assert.Equal("RUNASINVOKER", CompatibilityLayers.Without("WINXPSP2 RUNASINVOKER"));
    }

    [Theory]
    [InlineData(new[] { "WINXPSP2" }, "the WINXPSP2 compatibility mode")]
    [InlineData(new[] { "WIN98", "WINXPSP2" }, "the WIN98 and WINXPSP2 compatibility modes")]
    [InlineData(
        new[] { "WIN95", "WIN98", "WINXPSP2" },
        "the WIN95, WIN98 and WINXPSP2 compatibility modes")]
    public void Describe_names_every_mode_that_the_value_holds(string[] modes, string expected)
    {
        Assert.Equal(expected, CompatibilityLayers.Describe(modes));
    }
}
