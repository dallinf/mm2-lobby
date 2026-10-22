namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// The reason a ZeroTier command gave no answer. Each reason has its own fix,
/// so the app must keep the three reasons apart. A player who reads the wrong
/// reason looks for the wrong fix.
/// </summary>
public enum ZeroTierFault
{
    /// <summary>
    /// The command did not run. The app found no command file in an install
    /// folder, and Windows did not find the plain name either.
    /// </summary>
    CommandNotFound,

    /// <summary>
    /// The command ran, and ZeroTier refused it. ZeroTier cannot read its own
    /// authentication token with the rights of this player.
    /// </summary>
    PermissionDenied,

    /// <summary>
    /// The command ran, and the app knows no more. The ZeroTier service is
    /// stopped, or ZeroTier answered in a form that the app cannot read.
    /// </summary>
    Unknown,
}
