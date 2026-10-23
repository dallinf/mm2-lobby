namespace Mm2Lobby.Core.Launch;

/// <summary>One line for the player, and one step that the player can take.</summary>
public sealed record LobbyLaunchAdvice(string Message, string PlayerAction);

/// <summary>
/// The text that the window shows after a failed lobby launch. The text speaks
/// to a player who knows nothing about DirectPlay, so it names an action and
/// not a code.
/// </summary>
public static class LobbyLaunchMessages
{
    /// <summary>This method gives the text of one fault.</summary>
    public static LobbyLaunchAdvice For(LobbyLaunchFault fault) => fault switch
    {
        LobbyLaunchFault.NotWindows => new LobbyLaunchAdvice(
            "This computer does not run Windows, and MM2 needs Windows.",
            "Start MM2 Lobby on the Windows computer that holds the game."),

        LobbyLaunchFault.WrongProcessBitness => new LobbyLaunchAdvice(
            "This copy of MM2 Lobby cannot talk to DirectPlay.",
            "Ask the person who runs the group for the 32 bit copy of MM2 Lobby."),

        LobbyLaunchFault.DirectPlayMissing => new LobbyLaunchAdvice(
            "Windows has the DirectPlay component off, and MM2 needs it.",
            "Go back to the checks and choose Fix beside DirectPlay."),

        LobbyLaunchFault.GameNotRegistered => new LobbyLaunchAdvice(
            "Windows does not know how to start MM2 for a race.",
            "Go back to the checks and choose Fix beside the game."),

        LobbyLaunchFault.GameRegistrationWrong => new LobbyLaunchAdvice(
            "The settings that start MM2 for a race hold a wrong value.",
            "Go back to the checks and choose Fix beside the game."),

        LobbyLaunchFault.GameWillNotStart => new LobbyLaunchAdvice(
            "Windows did not let MM2 start.",
            "Right click midtown2.exe, choose Properties, open the Compatibility "
                + "tab, and clear every box. Then try again."),

        LobbyLaunchFault.DirectPlayRefused => new LobbyLaunchAdvice(
            "DirectPlay did not start the race.",
            "Start MM2 by hand from the main window, and send the report to the "
                + "person who runs the group."),

        LobbyLaunchFault.Unexpected => new LobbyLaunchAdvice(
            "MM2 Lobby met an error that it does not know.",
            "Start MM2 by hand from the main window, and send the report to the "
                + "person who runs the group."),

        _ => throw new ArgumentOutOfRangeException(
            nameof(fault), fault, "A successful launch needs no message."),
    };
}
