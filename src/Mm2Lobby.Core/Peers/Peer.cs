using System.Net;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// One row in the room list. The pair (IsOnline, AppRunning) gives the three
/// display states: in the lobby, online but the app is not running, offline.
/// </summary>
public sealed record Peer(
    string NodeId,
    string DisplayName,
    IPAddress? Address,
    bool IsOnline,
    bool AppRunning,
    bool Hosting,
    DateTimeOffset? StartedAt,
    LinkQuality Link);
