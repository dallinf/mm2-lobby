using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// Merges the peers heard over UDP with the nodes ZeroTier reports and the
/// names the app remembers, and sorts the result for display.
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

    public async Task<IReadOnlyList<Peer>> GetPeersAsync(
        string myNodeId,
        IReadOnlyDictionary<string, string> rememberedNames,
        CancellationToken ct)
    {
        var quality = await _zeroTier.GetLinkQualityAsync(ct);
        var peers = new List<Peer>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { myNodeId };

        foreach (var live in _table.GetLive())
        {
            if (!seen.Add(live.NodeId)) continue;

            peers.Add(new Peer(
                live.NodeId,
                live.Player,
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
            .ToList();
    }

    private static string NameOf(
        string nodeId, IReadOnlyDictionary<string, string> rememberedNames)
        => rememberedNames.TryGetValue(nodeId, out var name) ? name : nodeId;
}
