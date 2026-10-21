namespace Mm2Lobby.Core.Configuration;

public sealed class AppConfig
{
    public string DisplayName { get; set; } = string.Empty;

    public string GamePath { get; set; } = string.Empty;

    public string ZeroTierNetworkId { get; set; } = string.Empty;

    /// <summary>
    /// This property holds the last display name that the app received from
    /// each ZeroTier node ID. The lobby uses the map to show a peer whose app
    /// is closed.
    /// </summary>
    public Dictionary<string, string> RememberedNames { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
