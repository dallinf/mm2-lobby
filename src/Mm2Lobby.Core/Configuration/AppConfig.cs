using System.Collections.Concurrent;

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
    /// <remarks>
    /// The type is a ConcurrentDictionary because two threads reach this map.
    /// The pump thread adds a node ID in LobbyService.RememberNames, outside
    /// the lock of ConfigStore.Save. At the same moment the window can save,
    /// and JsonSerializer reads every entry of this map. A plain Dictionary
    /// throws InvalidOperationException in that moment, GamePathCheck then
    /// puts back the old game path, and the player loses the game that they
    /// just chose. A ConcurrentDictionary reads without that exception.
    /// </remarks>
    public ConcurrentDictionary<string, string> RememberedNames { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
