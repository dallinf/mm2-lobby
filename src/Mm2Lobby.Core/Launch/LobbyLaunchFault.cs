namespace Mm2Lobby.Core.Launch;

/// <summary>
/// The reason that a lobby launch failed. The app shows the player the text
/// that LobbyLaunchMessages gives for the value.
/// </summary>
public enum LobbyLaunchFault
{
    /// <summary>The launch worked.</summary>
    None,

    /// <summary>This computer does not run Windows.</summary>
    NotWindows,

    /// <summary>
    /// The app runs as 64 bit code. Windows 11 ships a 32 bit dplayx.dll only,
    /// so a 64 bit process cannot load DirectPlay.
    /// </summary>
    WrongProcessBitness,

    /// <summary>Windows has the DirectPlay component off.</summary>
    DirectPlayMissing,

    /// <summary>No registry key registers MM2 with the DirectPlay lobby.</summary>
    GameNotRegistered,

    /// <summary>
    /// A registry key registers MM2, and one value of the key stops the
    /// launch.
    /// </summary>
    GameRegistrationWrong,

    /// <summary>
    /// Windows did not start the game. A compatibility layer on the game makes
    /// Windows demand elevation, and DirectPlay cannot answer that demand.
    /// </summary>
    GameWillNotStart,

    /// <summary>DirectPlay refused a call, and the app cannot name a cause.</summary>
    DirectPlayRefused,

    /// <summary>The launcher caught an error that it did not expect.</summary>
    Unexpected,
}
