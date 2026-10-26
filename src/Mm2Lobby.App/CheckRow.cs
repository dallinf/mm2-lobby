using Mm2Lobby.Core.Preflight;

namespace Mm2Lobby.App;

/// <summary>One line of the setup list, already in display text.</summary>
/// <param name="Name">The name of the check.</param>
/// <param name="Mark">The state of the check, in one word.</param>
/// <param name="Message">The full message of the check. The window wraps it.</param>
/// <param name="Advice">The full advice of the check, or the repair hint.</param>
/// <param name="ActionLabel">The text of the button.</param>
/// <param name="ShowAction">True when the window shows the button.</param>
public sealed record CheckRow(
    string Name,
    string Mark,
    string Message,
    string Advice,
    string ActionLabel,
    bool ShowAction)
{
    /// <param name="result">The result that the check returned.</param>
    /// <param name="repairHint">
    /// The hint for a check that offers a repair while it passes. A null value
    /// means that the check offers no action while it passes.
    /// </param>
    public static CheckRow From(CheckResult result, string? repairHint)
    {
        var passed = result.Status == CheckStatus.Pass;

        // A check that passes can still need a repair. The firewall check
        // cannot see that a rule names an old game path. Then the check
        // passes, and the firewall still blocks the game. A window that shows
        // Fix only beside a failed check never reaches that repair.
        return new CheckRow(
            result.Name,
            MarkOf(result.Status, result.Blocks),
            result.Message,
            (passed ? repairHint : result.FixDescription) ?? string.Empty,
            passed ? "Repair" : "Fix",
            !passed || repairHint is not null);
    }

    /// <summary>
    /// Builds a row for a check whose last result no longer holds. The player
    /// changed the ZeroTier network ID, so the ZeroTier check ran against
    /// another network. A row that still showed its old Pass would assert
    /// something false.
    /// </summary>
    /// <param name="name">The name of the check.</param>
    public static CheckRow Stale(string name) => new(
        name,
        MarkOf(CheckStatus.Unavailable, blocks: true),
        "The network ID changed. Choose Check again.",
        string.Empty,
        "Fix",

        // The row offers no button. A fix for a result that the app no longer
        // has would run against an unknown state.
        false);

    /// <param name="status">The state that the check reported.</param>
    /// <param name="blocks">
    /// False for an advisory check. The window shows an advisory row beside
    /// the others, so the mark must say that the race goes on. A row that read
    /// Problem would tell the player that the app stopped, and the app did not
    /// stop.
    /// </param>
    private static string MarkOf(CheckStatus status, bool blocks) => (status, blocks) switch
    {
        (CheckStatus.Pass, _) => "OK",
        (_, false) => "Optional",
        (CheckStatus.Fail, _) => "Problem",
        _ => "Unknown",
    };
}
