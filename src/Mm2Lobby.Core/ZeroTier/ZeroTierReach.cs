namespace Mm2Lobby.Core.ZeroTier;

/// <summary>
/// What the last cycle learned about ZeroTier. The lobby needs the virtual
/// network for every announcement, so these three values are three different
/// facts and the window gives each one its own sentence.
/// </summary>
/// <remarks>
/// The client answers null for every failure, and it throws nothing. So a
/// window that reads the peer list alone cannot tell "the app sees nobody"
/// from "nobody hosts a race". The first sentence sends the player to his own
/// ZeroTier, and the second sends him to a friend.
/// </remarks>
public enum ZeroTierReach
{
    /// <summary>
    /// ZeroTier gave no answer. The service is stopped, or the account cannot
    /// read the authentication token of ZeroTier, or the command is missing.
    /// The app has read nothing yet before its first cycle, and it takes this
    /// value then as well.
    /// </summary>
    NoAnswer,

    /// <summary>
    /// ZeroTier answered, and this computer holds no address on the network of
    /// the group. The app cannot announce itself, because an announcement
    /// carries the address.
    /// </summary>
    NoAddress,

    /// <summary>
    /// ZeroTier answered, and this computer holds an address on the network of
    /// the group. The app announces itself and it hears every friend.
    /// </summary>
    OnTheNetwork,
}
