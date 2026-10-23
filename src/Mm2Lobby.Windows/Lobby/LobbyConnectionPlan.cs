using Mm2Lobby.Core.Launch;

namespace Mm2Lobby.Windows.Lobby;

/// <summary>
/// One call of RunApplication. The label names the attempt in the report, and
/// the two flags say which fields of DPLCONNECTION the launcher fills.
/// </summary>
internal sealed record LobbyConnectionAttempt(
    string Label,
    uint Flags,
    bool SendServiceProvider,
    bool SendAddress);

/// <summary>
/// This class decides which fields of DPLCONNECTION a host and a player who
/// joins each send, and in which order the launcher tries them.
/// <para>
/// The order comes from the spike. The host role reached a game on the first
/// attempt. The join role also reached a game, and the output of that run was
/// lost, so nobody knows which attempt carried it. The launcher therefore
/// keeps every attempt that ran, in the order that it ran.
/// </para>
/// <para>
/// The host role tries the second attempt, which sends the service provider
/// GUID and no address. The join role skips that attempt, because a join needs
/// the address of the host. A game that starts with no address shows the player
/// its own menus, and the launcher would report a success.
/// </para>
/// </summary>
internal static class LobbyConnectionPlan
{
    /// <summary>This method gives the DPLCONNECTION flag of one role.</summary>
    public static uint FlagsFor(LobbyRole role) => role switch
    {
        LobbyRole.Host => LobbyConnectionFlags.CreateSession,
        LobbyRole.Join => LobbyConnectionFlags.JoinSession,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "The role has no flag."),
    };

    /// <summary>
    /// This method lists the attempts of one role, in order. The launcher stops
    /// at the first attempt that starts the game.
    /// </summary>
    public static IReadOnlyList<LobbyConnectionAttempt> AttemptsFor(LobbyRole role, bool hasAddress)
    {
        var flags = FlagsFor(role);
        var attempts = new List<LobbyConnectionAttempt>();

        if (hasAddress)
        {
            attempts.Add(new LobbyConnectionAttempt(
                "attempt 1, the provider GUID and the address together", flags, true, true));
        }

        if (role == LobbyRole.Host)
        {
            attempts.Add(new LobbyConnectionAttempt(
                "attempt 2, the provider GUID alone", flags, true, false));
        }

        if (hasAddress)
        {
            attempts.Add(new LobbyConnectionAttempt(
                "attempt 3, the address alone and an empty provider GUID", flags, false, true));
        }

        return attempts;
    }
}
