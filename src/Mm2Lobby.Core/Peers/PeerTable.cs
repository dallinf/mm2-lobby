using System.Net;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// This class holds the peers that sent an announcement recently. It rejects
/// every other datagram. The socket listens on 0.0.0.0 to receive the directed
/// broadcast. The home network of the player can also send data to the port.
/// The subnet check decides whether the app trusts a sender.
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
    /// This method checks one datagram and stores the peer. It returns false
    /// when the datagram breaks a rule. The caller can ignore this result. A
    /// rejected datagram is a normal event. It is not an exception.
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
            // A datagram can hold "appVersion":null. That value parses to a
            // null here. LivePeer.AppVersion does not accept a null.
            message.AppVersion ?? string.Empty,
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
