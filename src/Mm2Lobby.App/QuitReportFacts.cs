namespace Mm2Lobby.App;

/// <summary>
/// What the window holds when the player closes the app. QuitReport reads
/// these facts and writes the text of the report.
/// </summary>
/// <remarks>
/// Every value here is a value that the window shows on screen. That rule
/// keeps the decision simple, and it keeps a secret out of the file by
/// construction: the app never reads the authentication token of ZeroTier, so
/// no property can carry it.
/// </remarks>
public sealed record QuitReportFacts
{
    /// <summary>The time of the write.</summary>
    public DateTimeOffset Time { get; init; }

    /// <summary>The version of this app.</summary>
    public string AppVersion { get; init; } = string.Empty;

    /// <summary>The name that the app announces.</summary>
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>The ID of the ZeroTier network, as the box shows it.</summary>
    public string NetworkId { get; init; } = string.Empty;

    /// <summary>
    /// True while the app can put the game in the race. The two advisory
    /// checks and the launcher answer for this value.
    /// </summary>
    public bool CanStartTheGameInTheRace { get; init; }

    /// <summary>What the app reached, in one sentence.</summary>
    public string Reach { get; init; } = string.Empty;

    /// <summary>The title of the host panel.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The address that the window showed, or an empty text.</summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>The rows of the setup list.</summary>
    public IReadOnlyList<CheckRow> Checks { get; init; } = Array.Empty<CheckRow>();

    /// <summary>The rows of the lobby list.</summary>
    public IReadOnlyList<PeerRow> Players { get; init; } = Array.Empty<PeerRow>();

    /// <summary>The last message about a start, a fix, or the game.</summary>
    public string StatusMessage { get; init; } = string.Empty;

    /// <summary>The exception of the last failed cycle, as the window shows it.</summary>
    public string FaultMessage { get; init; } = string.Empty;

    /// <summary>The trail of calls of the last launch through the lobby.</summary>
    public string LaunchReport { get; init; } = string.Empty;
}
