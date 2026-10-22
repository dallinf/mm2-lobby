using System.Collections.Concurrent;

namespace Mm2Lobby.Core.Configuration;

/// <summary>
/// The configuration of the app. The process holds one instance, and the
/// window owns it.
/// </summary>
/// <remarks>
/// This class is not sealed, and DisplayName is virtual, for one reason: a
/// test answers a different name for each read of that property. Only such a
/// test can prove that AnnounceAsync reads the name one time. Two reads of the
/// name take one value for the length test and another value for the
/// truncation, and the truncation then throws.
/// </remarks>
public class AppConfig
{
    public virtual string DisplayName { get; set; } = string.Empty;

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
