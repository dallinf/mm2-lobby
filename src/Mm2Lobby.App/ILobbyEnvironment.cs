using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Lobby;

namespace Mm2Lobby.App;

/// <summary>
/// The system that the window drives. LobbyEnvironment is the real
/// implementation. A test supplies another implementation, so no test needs
/// Windows, a real network, a real process, or a real registry.
/// </summary>
public interface ILobbyEnvironment
{
    /// <summary>
    /// The one configuration object of the process. Every check and every
    /// lobby service read and write this object. With a second copy, a save
    /// from one component discards a value that another component wrote.
    /// </summary>
    AppConfig Config { get; }

    /// <summary>
    /// The version of this app. The window compares the version of each peer
    /// against this value.
    /// </summary>
    string AppVersion { get; }

    /// <summary>Builds the preflight checks, in the order that the window shows them.</summary>
    IReadOnlyList<CheckEntry> CreateChecks();

    /// <summary>
    /// Builds a new lobby service. LobbyService.StartAsync accepts one call,
    /// and a failed start leaves the instance unusable. So the window asks for
    /// a new service for each attempt.
    /// </summary>
    LobbyService CreateLobby();

    /// <summary>Writes Config to the file. This method can throw. The caller catches.</summary>
    void SaveConfig();

    /// <summary>Starts MM2. This method can throw. The caller catches.</summary>
    void LaunchGame(string gamePath);
}
