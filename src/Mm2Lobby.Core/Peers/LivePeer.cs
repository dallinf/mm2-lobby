using System.Net;

namespace Mm2Lobby.Core.Peers;

/// <summary>One peer the app has heard from, and when it last heard.</summary>
public sealed record LivePeer(
    string NodeId,
    string Player,
    IPAddress Address,
    bool Hosting,
    DateTimeOffset? StartedAt,
    string AppVersion,
    DateTimeOffset HeardAt);
