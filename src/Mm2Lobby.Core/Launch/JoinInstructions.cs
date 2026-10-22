namespace Mm2Lobby.Core.Launch;

/// <summary>
/// This class holds the steps that the window shows after the app starts the game. MM2 shows its own dialog windows.
/// These windows can refuse Ctrl+V. The window therefore tells the player to type the address.
/// </summary>
public static class JoinInstructions
{
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
}
