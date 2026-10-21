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

    public Task<ZeroTierStatus?> GetStatusAsync(CancellationToken ct)
        => Task.FromResult(Status);

    public Task<ZeroTierNetwork?> GetNetworkAsync(string networkId, CancellationToken ct)
        => Task.FromResult(Network);

    public Task<IReadOnlyDictionary<string, LinkQuality>> GetLinkQualityAsync(
        CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, LinkQuality>>(Quality);
}
