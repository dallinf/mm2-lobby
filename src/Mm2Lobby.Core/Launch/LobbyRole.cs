namespace Mm2Lobby.Core.Launch;

/// <summary>The part that one player takes in a race.</summary>
public enum LobbyRole
{
    /// <summary>This player starts the race, and the other players join it.</summary>
    Host,

    /// <summary>This player joins the race of another player.</summary>
    Join,
}
