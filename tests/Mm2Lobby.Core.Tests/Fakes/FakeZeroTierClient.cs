using System.Net;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Tests.Fakes;

public sealed class FakeZeroTierClient : IZeroTierClient
{
    public ZeroTierStatus? Status { get; set; } = new(true, "a09acf0233");

    public ZeroTierNetwork? Network { get; set; } =
        new("0123456789abcdef", "OK", IPAddress.Parse("10.147.17.42"), 24);

    public Dictionary<string, LinkQuality> Quality { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The network ID of the last call to GetNetworkAsync.</summary>
    public string? RequestedNetworkId { get; private set; }

    public Task<ZeroTierStatus?> GetStatusAsync(CancellationToken ct)
        => Task.FromResult(Status);

    /// <summary>
    /// Answers Network only for the network that Network names. ZeroTier knows
    /// nothing about another network, so this fake answers null for one, as
    /// zerotier-cli does.
    /// </summary>
    public Task<ZeroTierNetwork?> GetNetworkAsync(string networkId, CancellationToken ct)
    {
        RequestedNetworkId = networkId;

        var network = Network;
        if (network is not null && !string.Equals(
                network.NetworkId, networkId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<ZeroTierNetwork?>(null);
        }

        return Task.FromResult(network);
    }

    public Task<IReadOnlyDictionary<string, LinkQuality>> GetLinkQualityAsync(
        CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, LinkQuality>>(Quality);
}
