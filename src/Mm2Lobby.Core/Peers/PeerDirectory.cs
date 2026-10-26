using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// This class merges the peers that sent an announcement with the nodes
/// ZeroTier reports and the names the app remembers. It then sorts the peers
/// for the display.
/// </summary>
public sealed class PeerDirectory
{
    private readonly IZeroTierClient _zeroTier;
    private readonly PeerTable _table;

    public PeerDirectory(IZeroTierClient zeroTier, PeerTable table)
    {
        _zeroTier = zeroTier;
        _table = table;
    }

    /// <param name="myNodeId">The ZeroTier node ID of this machine.</param>
    /// <param name="networkId">
    /// The ZeroTier network ID. This method leaves the controller of that
    /// network out of the list. See NetworkIdRule.ControllerNodeId. A value
    /// that is not a valid network ID names no controller, and this method
    /// then leaves out no node.
    /// </param>
    /// <param name="rememberedNames">
    /// The names the app remembers, by node ID. This method looks up a
    /// lower-case node ID, so the caller must supply a map with the comparer
    /// StringComparer.OrdinalIgnoreCase.
    /// </param>
    /// <param name="ct">The token that cancels the ZeroTier read.</param>
    public async Task<IReadOnlyList<Peer>> GetPeersAsync(
        string myNodeId,
        string networkId,
        IReadOnlyDictionary<string, string> rememberedNames,
        CancellationToken ct)
    {
        var quality = await _zeroTier.GetLinkQualityAsync(ct);
        var peers = new List<Peer>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { myNodeId };

        // The controller of the network answers listpeers as a LEAF, because a
        // member sees it as a leaf. The LEAF filter of ZeroTierClient therefore
        // keeps it, and the controller reached the list as a row with a bare
        // node ID for a name. This set holds the controller from the start, so
        // every route below steps over it.
        var controller = NetworkIdRule.ControllerNodeId(networkId);
        if (controller.Length != 0) seen.Add(controller);

        foreach (var live in _table.GetLive())
        {
            if (!seen.Add(live.NodeId)) continue;

            peers.Add(new Peer(
                live.NodeId,
                live.Player,
                live.AppVersion,
                live.Address,
                IsOnline: true,
                AppRunning: true,
                live.Hosting,
                live.StartedAt,
                quality.TryGetValue(live.NodeId, out var link) ? link : LinkQuality.Unknown));
        }

        foreach (var (nodeId, link) in quality)
        {
            if (!seen.Add(nodeId)) continue;

            peers.Add(new Peer(
                nodeId,
                NameOf(nodeId, rememberedNames),
                AppVersion: string.Empty,
                Address: null,
                IsOnline: true,
                AppRunning: false,
                Hosting: false,
                StartedAt: null,
                link));
        }

        foreach (var (nodeId, name) in rememberedNames)
        {
            if (!seen.Add(nodeId)) continue;

            peers.Add(new Peer(
                nodeId,
                name,
                AppVersion: string.Empty,
                Address: null,
                IsOnline: false,
                AppRunning: false,
                Hosting: false,
                StartedAt: null,
                LinkQuality.Unknown));
        }

        return peers
            .OrderByDescending(peer => peer.Hosting)
            .ThenByDescending(peer => peer.AppRunning)
            .ThenByDescending(peer => peer.IsOnline)
            .ThenBy(peer => peer.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(peer => peer.NodeId, StringComparer.Ordinal)
            .ToList();
    }

    private static string NameOf(
        string nodeId, IReadOnlyDictionary<string, string> rememberedNames)
        => rememberedNames.TryGetValue(nodeId, out var name) ? name : nodeId;
}
