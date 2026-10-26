using System.Globalization;

namespace Mm2Lobby.App;

/// <summary>
/// This class builds the text of the report that the app writes when the
/// player closes it. It is a pure function: it reads the facts, it writes no
/// file, and it throws nothing.
/// </summary>
/// <remarks>
/// The owner of the group asked the players for lines of the panel that holds
/// the trail of a DirectPlay launch. A trail with no state around it is hard
/// to read, so this report carries the state that the window shows beside
/// that trail: the time, the version, the setup rows, the network, the race
/// and the list of players.
/// </remarks>
public static class QuitReport
{
    /// <summary>
    /// The line end of the file. The owner reads the report on Windows, and an
    /// editor of Windows can show one long line for a file that holds the line
    /// end of Unix alone. The build and the tests run on macOS as well, so
    /// this value is a constant and not the line end of this computer.
    /// </summary>
    public const string LineEnd = "\r\n";

    /// <summary>The form of the time. Every reader of the file reads one form.</summary>
    private const string TimeForm = "yyyy-MM-dd HH:mm:ss zzz";

    /// <summary>Builds the text of the report.</summary>
    /// <param name="facts">What the window holds at the close.</param>
    public static string Build(QuitReportFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var lines = new List<string>
        {
            "MM2 Lobby wrote this report when the player closed the app.",
            string.Empty,
            $"Time: {facts.Time.ToString(TimeForm, CultureInfo.InvariantCulture)}",
            $"App version: {facts.AppVersion}",
            $"Player name: {facts.PlayerName}",
            $"ZeroTier network ID: {facts.NetworkId}",
            "The app can start the game in the race: "
                + (facts.CanStartTheGameInTheRace ? "yes" : "no"),
            $"The network: {facts.Reach}",
            string.Empty,
            "Setup",
        };

        // Each empty part takes a sentence of its own. A heading with no line
        // under it leaves the reader with two answers: the part was empty, or
        // the app lost it.
        if (facts.Checks.Count == 0) lines.Add("The app ran no check.");
        lines.AddRange(facts.Checks.Select(LineOf));

        lines.Add(string.Empty);
        lines.Add("The race");
        lines.Add($"Title: {facts.Title}");
        lines.Add(facts.Address.Length > 0
            ? $"Address: {facts.Address}"
            : "The window showed no address.");

        lines.Add(string.Empty);
        lines.Add("The players");
        if (facts.Players.Count == 0) lines.Add("The list held no player.");
        lines.AddRange(facts.Players.Select(LineOf));

        lines.Add(string.Empty);
        lines.Add("The last message");
        if (facts.StatusMessage.Length > 0) lines.Add($"Status: {facts.StatusMessage}");
        if (facts.FaultMessage.Length > 0) lines.Add($"Fault: {facts.FaultMessage}");
        if (facts.StatusMessage.Length == 0 && facts.FaultMessage.Length == 0)
        {
            lines.Add("The window showed no message.");
        }

        lines.Add(string.Empty);
        lines.Add("The report of the last start");
        lines.Add(facts.LaunchReport.Length > 0
            ? facts.LaunchReport
            : "The app started no game through the lobby.");

        return WithTheLineEndOfWindows(string.Join(LineEnd, lines) + LineEnd);
    }

    /// <summary>One line of the setup list. The advice holds no line of its own.</summary>
    private static string LineOf(CheckRow row) => row.Advice.Length > 0
        ? $"{row.Name} | {row.Mark} | {row.Message} | {row.Advice}"
        : $"{row.Name} | {row.Mark} | {row.Message}";

    /// <summary>One line of the lobby list.</summary>
    private static string LineOf(PeerRow row) => row.VersionDiffers
        ? $"{row.Name} | {row.State} | {row.Address} | {row.Link} | {row.VersionNote}"
        : $"{row.Name} | {row.State} | {row.Address} | {row.Link}";

    /// <summary>
    /// Gives every line of the text the line end of Windows. A value that the
    /// window holds can carry a line end of its own: the trail of a launch
    /// joins its steps with the line end of the computer that built it, and
    /// the message of an exception can hold one too.
    /// </summary>
    private static string WithTheLineEndOfWindows(string text) => text
        .Replace("\r\n", "\n")
        .Replace("\r", "\n")
        .Replace("\n", LineEnd);
}
