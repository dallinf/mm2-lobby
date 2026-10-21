namespace Mm2Lobby.Core.ZeroTier;

public interface IZeroTierClient
{
    Task<ZeroTierStatus?> GetStatusAsync(CancellationToken ct);

    Task<ZeroTierNetwork?> GetNetworkAsync(string networkId, CancellationToken ct);

    Task<IReadOnlyDictionary<string, LinkQuality>> GetLinkQualityAsync(CancellationToken ct);
}
