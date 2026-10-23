namespace Mm2Lobby.Core.Launch;

/// <summary>
/// What the app asks the lobby launcher to do. The app builds a request with
/// ToHost or ToJoin, and the launcher reads it.
/// </summary>
public sealed record LobbyLaunchRequest
{
    /// <summary>The launcher sends this name when the player gave no name.</summary>
    public const string DefaultPlayerName = "Player";

    /// <summary>The launcher sends this name when the host named no race.</summary>
    public const string DefaultSessionName = "MM2 Lobby race";

    private LobbyLaunchRequest(
        LobbyRole role,
        string playerName,
        string sessionName,
        string? hostAddress)
    {
        Role = role;
        PlayerName = playerName;
        SessionName = sessionName;
        HostAddress = hostAddress;
    }

    /// <summary>The part that this player takes.</summary>
    public LobbyRole Role { get; }

    /// <summary>The name that the other players see.</summary>
    public string PlayerName { get; }

    /// <summary>The name of the race.</summary>
    public string SessionName { get; }

    /// <summary>
    /// The address of the computer that holds the race. It is null for a host,
    /// and it holds an address for a player who joins.
    /// </summary>
    public string? HostAddress { get; }

    /// <summary>This method builds a request that starts a new race.</summary>
    public static LobbyLaunchRequest ToHost(string? playerName = null, string? sessionName = null) =>
        new(
            LobbyRole.Host,
            Or(playerName, DefaultPlayerName),
            Or(sessionName, DefaultSessionName),
            null);

    /// <summary>
    /// This method builds a request that joins the race at an address. The app
    /// offers Join only after it knows the address of the host, so a blank
    /// address is a fault in the app and not a fault of the player.
    /// </summary>
    public static LobbyLaunchRequest ToJoin(string hostAddress, string? playerName = null)
    {
        if (string.IsNullOrWhiteSpace(hostAddress))
        {
            throw new ArgumentException("The address of the host is empty.", nameof(hostAddress));
        }

        return new LobbyLaunchRequest(
            LobbyRole.Join,
            Or(playerName, DefaultPlayerName),
            DefaultSessionName,
            hostAddress.Trim());
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
