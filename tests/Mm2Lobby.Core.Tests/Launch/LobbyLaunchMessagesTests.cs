using Mm2Lobby.Core.Launch;

namespace Mm2Lobby.Core.Tests.Launch;

public class LobbyLaunchMessagesTests
{
    private static IEnumerable<LobbyLaunchFault> Faults() =>
        Enum.GetValues<LobbyLaunchFault>().Where(one => one != LobbyLaunchFault.None);

    [Fact]
    public void Every_fault_gives_the_player_a_message_and_an_action()
    {
        // The window shows both lines. A blank line leaves the player with a
        // failure and no next step.
        foreach (var fault in Faults())
        {
            var advice = LobbyLaunchMessages.For(fault);

            Assert.False(string.IsNullOrWhiteSpace(advice.Message), $"{fault} gave a blank message.");
            Assert.False(string.IsNullOrWhiteSpace(advice.PlayerAction), $"{fault} gave a blank action.");
        }
    }

    [Fact]
    public void The_fault_None_has_no_message()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LobbyLaunchMessages.For(LobbyLaunchFault.None));
    }

    [Fact]
    public void The_message_of_each_fault_differs_from_the_message_of_the_others()
    {
        var messages = Faults().Select(one => LobbyLaunchMessages.For(one).Message).ToList();

        Assert.Equal(messages.Count, messages.Distinct(StringComparer.Ordinal).Count());
    }
}
