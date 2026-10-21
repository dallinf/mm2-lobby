using System.Net;

namespace Mm2Lobby.Core.ZeroTier;

public sealed record ZeroTierNetwork(
    string NetworkId,
    string Status,
    IPAddress? Address,
    int PrefixLength)
{
    public bool IsJoined => Status == "OK" && Address is not null;
}
