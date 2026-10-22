using System.Net;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// This record holds one row of the room list. The pair (IsOnline, AppRunning)
/// gives the three display states. The states are: in the lobby; online, but
/// the app is not running; offline. AppVersion is the empty string for a peer
/// that sent no announcement.
/// </summary>
public sealed record Peer(
    string NodeId,
    string DisplayName,
    string AppVersion,
    IPAddress? Address,
    bool IsOnline,
    bool AppRunning,
    bool Hosting,
    DateTimeOffset? StartedAt,
    LinkQuality Link);
