namespace Mm2Lobby.Core.Launch;

/// <summary>
/// An implementation starts MM2 in a multiplayer session. The player then types
/// no address, because the lobby gives the address to the game.
/// <para>
/// DirectPlayLobbyLauncher in Mm2Lobby.Windows is the real implementation.
/// Mm2Lobby.Core therefore holds no Windows API and tests on macOS.
/// </para>
/// </summary>
public interface ILobbyLauncher
{
    /// <summary>
    /// True when this computer and this process can reach DirectPlay. The
    /// window reads this value before it offers a one click start.
    /// </summary>
    bool CanLaunch { get; }

    /// <summary>
    /// This method starts the game. It throws nothing that the app must catch:
    /// a failure comes back as a result that names the fault and the step for
    /// the player.
    /// <para>
    /// The app keeps the session of a successful result for as long as the game
    /// may run.
    /// </para>
    /// </summary>
    LobbyLaunchResult Launch(LobbyLaunchRequest request);
}
