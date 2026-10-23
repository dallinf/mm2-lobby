namespace Mm2Lobby.Core.Launch;

/// <summary>
/// One live lobby session. DirectPlay started the game, and the game asks
/// DirectPlay for its settings about three to four seconds later. The session
/// owns the DirectPlay interface and the memory that carries those settings.
/// <para>
/// The app holds the session for as long as the game may run. A successful
/// launch gives the app this object, and no other type gives the app the
/// memory, so the app cannot drop the memory and keep the game.
/// </para>
/// <para>
/// WARNING: do not dispose the session while the game runs. Dispose releases
/// the DirectPlay interface, and the game then falls back to its own menus.
/// The session refuses the release while the game process runs, and the app
/// must not depend on that refusal.
/// </para>
/// </summary>
public interface ILobbySession : IDisposable
{
    /// <summary>True while the session holds the DirectPlay interface.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// The count of memory blocks that the session keeps for DirectPlay. The
    /// session frees none of them.
    /// </summary>
    int KeptBlockCount { get; }

    /// <summary>
    /// This method waits for the lobby message of the game. DirectPlay signals
    /// an event when the game reaches the lobby, and that signal is the only
    /// direct evidence that the game connected.
    /// <para>
    /// The method returns false after the timeout. A false answer does not
    /// prove a failure, because the game can reach the lobby later.
    /// </para>
    /// </summary>
    bool WaitForGame(TimeSpan timeout);
}
