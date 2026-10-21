using System.Net;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// One peer that sent an announcement to the app. The record includes the time
/// of the last announcement.
/// </summary>
public sealed record LivePeer(
    string NodeId,
    string Player,
    IPAddress Address,
    bool Hosting,
    DateTimeOffset? StartedAt,
    string AppVersion,
    DateTimeOffset HeardAt);
