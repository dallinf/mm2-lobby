namespace Mm2Lobby.Core.Configuration;

public sealed class AppConfig
{
    public string DisplayName { get; set; } = string.Empty;

    public string GamePath { get; set; } = string.Empty;

    public string ZeroTierNetworkId { get; set; } = string.Empty;

    /// <summary>
    /// Maps a ZeroTier node ID to the last display name heard from it, so a
    /// friend whose app is closed still appears by name.
    /// </summary>
    public Dictionary<string, string> RememberedNames { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
