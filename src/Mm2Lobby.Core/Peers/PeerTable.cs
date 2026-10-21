using System.Net;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// Holds the peers heard recently and rejects everything else. The socket binds
/// to 0.0.0.0 to receive the directed broadcast, so the player's home network
/// can also reach the port. The subnet test is the authorisation boundary.
/// </summary>
public sealed class PeerTable
{
    public const int MaxPlayerNameLength = 32;

    private readonly TimeSpan _expiry;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, LivePeer> _peers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public PeerTable(TimeSpan expiry, Func<DateTimeOffset> clock)
    {
        _expiry = expiry;
        _clock = clock;
    }

    /// <summary>
    /// Validates one datagram and records it. Returns false when the datagram
    /// fails any rule, which the caller may ignore: a rejected datagram is a
    /// normal event, not an error.
    /// </summary>
    public bool Accept(ReadOnlySpan<byte> datagram, IPAddress source, IPNetwork allowed)
    {
        if (!allowed.Contains(source)) return false;

        var message = Announcement.TryParse(datagram);
        if (message is null) return false;
        if (message.Proto != Announcement.CurrentProto) return false;
        if (string.IsNullOrWhiteSpace(message.NodeId)) return false;
        if (string.IsNullOrWhiteSpace(message.Player)) return false;
        if (message.Player.Length > MaxPlayerNameLength) return false;
        if (!IPAddress.TryParse(message.Address, out var claimed)) return false;
        if (!claimed.Equals(source)) return false;

        var peer = new LivePeer(
            message.NodeId.ToLowerInvariant(),
            message.Player,
            source,
            message.Hosting,
            message.StartedAt,
            message.AppVersion,
            _clock());

        lock (_gate)
        {
            _peers[peer.NodeId] = peer;
        }

        return true;
    }

    public IReadOnlyList<LivePeer> GetLive()
    {
        var cutoff = _clock() - _expiry;

        lock (_gate)
        {
            return _peers.Values.Where(peer => peer.HeardAt > cutoff).ToList();
        }
    }
}
