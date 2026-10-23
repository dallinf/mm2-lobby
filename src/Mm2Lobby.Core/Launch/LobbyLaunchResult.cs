namespace Mm2Lobby.Core.Launch;

/// <summary>
/// The answer of the lobby launcher. A success carries the session that the app
/// must hold. A failure carries a message for the player, a step that the
/// player can take, and the trail of calls for a report.
/// <para>
/// Only Success takes a session, and only Failure takes a fault. The app
/// therefore cannot read a started game out of a failure, and it cannot drop
/// the session of a success.
/// </para>
/// </summary>
public sealed record LobbyLaunchResult
{
    private LobbyLaunchResult(
        ILobbySession? session,
        LobbyLaunchFault fault,
        string message,
        string? playerAction,
        string detail)
    {
        Session = session;
        Fault = fault;
        Message = message;
        PlayerAction = playerAction;
        Detail = detail;
    }

    /// <summary>True when DirectPlay started the game.</summary>
    public bool Started => Fault == LobbyLaunchFault.None;

    /// <summary>
    /// The live session. It holds a value after a success, and it is null after
    /// a failure.
    /// </summary>
    public ILobbySession? Session { get; }

    /// <summary>The reason that the launch failed.</summary>
    public LobbyLaunchFault Fault { get; }

    /// <summary>One line for the player.</summary>
    public string Message { get; }

    /// <summary>The step that the player can take, or null.</summary>
    public string? PlayerAction { get; }

    /// <summary>
    /// The trail of calls, with the code of each DirectPlay answer. The app
    /// puts this text in a report and does not show it to the player.
    /// </summary>
    public string Detail { get; }

    /// <summary>This method reports that DirectPlay started the game.</summary>
    public static LobbyLaunchResult Success(ILobbySession session, string detail)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new LobbyLaunchResult(
            session,
            LobbyLaunchFault.None,

            // The player reads "race" everywhere else in the window, and the
            // steps beside this sentence use that word too. One meaning takes
            // one word, so this sentence does not say "session" as well.
            // "Session" stays in the names of the code and in the comments.
            "MM2 Lobby started the game in the race.",
            null,
            detail);
    }

    /// <summary>This method reports a failure, with the text for the player.</summary>
    public static LobbyLaunchResult Failure(
        LobbyLaunchFault fault,
        string message,
        string? playerAction,
        string detail)
    {
        if (fault == LobbyLaunchFault.None)
        {
            throw new ArgumentException("A failure needs a fault.", nameof(fault));
        }

        return new LobbyLaunchResult(null, fault, message, playerAction, detail);
    }

    /// <summary>
    /// This method reports a failure with the text that the Core holds for the
    /// fault.
    /// </summary>
    public static LobbyLaunchResult Failure(LobbyLaunchFault fault, string detail)
    {
        var advice = LobbyLaunchMessages.For(fault);

        return Failure(fault, advice.Message, advice.PlayerAction, detail);
    }
}
