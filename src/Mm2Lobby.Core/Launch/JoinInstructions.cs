namespace Mm2Lobby.Core.Launch;

/// <summary>
/// This class holds the steps that the window shows after the app starts the
/// game. The steps follow the path that the app took.
/// <para>
/// The ordinary path starts MM2 and nothing more. MM2 shows its own dialogs,
/// these dialogs can refuse Ctrl+V, and the app therefore tells the player to
/// type the address.
/// </para>
/// <para>
/// The lobby path starts MM2 in the race. The game holds the address already,
/// so the two lists below that name the race tell the player to type nothing.
/// A window that showed the ordinary steps after a lobby launch would ask the
/// player for an action that the game does not offer him.
/// </para>
/// </summary>
public static class JoinInstructions
{
    /// <summary>
    /// The first step of each list for the lobby path. It tells the player
    /// that he types no address, which is the one thing that this path changes
    /// for him.
    /// </summary>
    private const string InTheRace = "MM2 Lobby put the game in the race. You type no address.";

    public static IReadOnlyList<string> For(string hostAddress) => new[]
    {
        "In MM2 choose Multiplayer.",
        "Choose TCP/IP, then Join.",
        $"Type the host address {hostAddress} and confirm.",
    };

    public static IReadOnlyList<string> ForHost() => new[]
    {
        "In MM2 choose Multiplayer.",
        "Choose TCP/IP, then Host.",
        "Pick a city and a race, then wait for the others.",
    };

    /// <summary>
    /// The steps for the host after a lobby launch. DirectPlay made the race,
    /// so the host opens no menu of MM2.
    /// </summary>
    public static IReadOnlyList<string> ForTheHostInTheRace() => new[]
    {
        InTheRace,
        "Pick a city and a race, then wait for the others.",
    };

    /// <summary>
    /// The steps for a player who joined, after a lobby launch. The game
    /// reached the race of the host, so this player waits.
    /// </summary>
    public static IReadOnlyList<string> ForThePlayerInTheRace() => new[]
    {
        InTheRace,
        "Wait for the host to start the race.",
    };
}
