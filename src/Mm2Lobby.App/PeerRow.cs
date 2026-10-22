using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.App;

/// <summary>One line of the lobby list, already in display text.</summary>
/// <param name="Name">The display name of the peer.</param>
/// <param name="State">What the peer does now.</param>
/// <param name="Address">The ZeroTier address, or a dash.</param>
/// <param name="Link">The link quality, or a dash.</param>
/// <param name="VersionDiffers">True when the peer runs another app version.</param>
/// <param name="VersionNote">The text for the warning column.</param>
public sealed record PeerRow(
    string Name,
    string State,
    string Address,
    string Link,
    bool VersionDiffers,
    string VersionNote)
{
    private const string NoValue = "—";

    /// <param name="peer">The peer that the lobby service reports.</param>
    /// <param name="myVersion">The version of this app.</param>
    public static PeerRow From(Peer peer, string myVersion)
    {
        // An empty version means that the peer sent no announcement. Then the
        // app of the peer is not running and no version exists to compare.
        var differs = peer.AppVersion.Length > 0
            && !string.Equals(peer.AppVersion, myVersion, StringComparison.Ordinal);

        return new PeerRow(
            peer.DisplayName,
            StateOf(peer),
            peer.Address?.ToString() ?? NoValue,
            LinkOf(peer),
            differs,
            differs ? $"Version {peer.AppVersion} is different." : string.Empty);
    }

    /// <summary>
    /// The pair (IsOnline, AppRunning) gives three states. The Hosting flag
    /// gives the fourth. The player must tell "the app is not running" apart
    /// from "offline", because the two need different help.
    /// </summary>
    private static string StateOf(Peer peer) => peer switch
    {
        { Hosting: true } => "Hosts a race",
        { AppRunning: true } => "In the lobby",
        { IsOnline: true } => "Online, the app is not running",
        _ => "Offline",
    };

    private static string LinkOf(Peer peer) => peer.Link switch
    {
        LinkQuality.Direct => "Direct",
        LinkQuality.Relayed => "Relayed",
        _ => NoValue,
    };
}
