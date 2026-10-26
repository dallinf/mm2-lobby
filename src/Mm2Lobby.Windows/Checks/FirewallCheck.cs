using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This check looks for the three firewall rules that this app adds. When the
/// firewall blocks the game, MM2 fails silently and reports only that it found
/// no host. When the firewall blocks this app, the player list stays empty and
/// no row of the window reports a problem. The check reads the game path from
/// the shared AppConfig when the fix writes the rules.
/// </summary>
/// <remarks>
/// Each rule carries its own name, so the check can see which one rule is
/// missing. The check does not read the text of the netsh output, because
/// Windows translates that text.
/// <para>
/// The game gets one rule for each direction. This app gets an inbound rule
/// alone. The app binds UDP port 48200 and reads the announcements of the
/// friends there. Windows raises its own prompt for a program that listens,
/// and a player who answers Cancel, or who holds no rights, gets a Block rule.
/// The announcements of that player still leave his computer, so every friend
/// sees him and he sees nobody. The inbound rule stops that prompt and that
/// state. Windows permits outbound traffic by default, and a rule that nobody
/// needs can name a program path that no longer exists, so this app adds no
/// outbound rule for itself.
/// </para>
/// <para>
/// The check sees only these three rules. A rule that Windows made when the
/// player chose Allow at the firewall prompt is not visible to the check. The check
/// also cannot see that a rule names an old program path. The fix changes each
/// rule that exists, so the fix repairs that case. It repairs a game that
/// moved, and it repairs this app on a drive that gives it a new path.
/// </para>
/// </remarks>
public sealed class FirewallCheck : ICheck
{
    public const string RuleName = "MM2 Lobby - Midtown Madness 2";
    public const string InboundRuleName = $"{RuleName} (inbound)";
    public const string OutboundRuleName = $"{RuleName} (outbound)";

    /// <summary>The rule of the app itself. The player reads these names in Windows.</summary>
    public const string AppRuleName = "MM2 Lobby - the lobby app";
    public const string AppInboundRuleName = $"{AppRuleName} (inbound)";

    // The check cannot see a rule that Windows made. The text says so, and it
    // says what the Fix button does in that case. The window enables Host and
    // Join only when every check passes, so the text must not offer a skip.
    private const string Scope =
        "The check sees only the three rules that this app adds. If Windows already has "
        + "an Allow rule for MM2, pressing Fix adds the app's own rules.";

    private readonly IProcessRunner _runner;
    private readonly IProcessRunner _elevatedRunner;
    private readonly AppConfig _config;
    private readonly string _appPath;

    /// <param name="runner">Reads the rules. It needs no administrator rights.</param>
    /// <param name="elevatedRunner">Changes the rules. It needs administrator rights.</param>
    /// <param name="config">The configuration that the window shares with the lobby.</param>
    /// <param name="appPath">
    /// The program file of this app. The app reads it from the operating
    /// system at the start, and the path cannot change while the app runs.
    /// </param>
    public FirewallCheck(
        IProcessRunner runner, IProcessRunner elevatedRunner, AppConfig config, string appPath)
    {
        _runner = runner;
        _elevatedRunner = elevatedRunner;
        _config = config;
        _appPath = appPath;
    }

    public string Name => "Windows Firewall";

    public async Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var rules = TheRules();
        var missing = new List<string>();

        foreach (var rule in rules)
        {
            if (!await RuleExistsAsync(rule.Name, ct)) missing.Add(rule.Traffic);
        }

        if (missing.Count == 0)
        {
            return new CheckResult(
                Name, CheckStatus.Pass, "The MM2 Lobby firewall rules are in place.", null);
        }

        var message = missing.Count switch
        {
            1 => $"The MM2 Lobby firewall rule for {missing[0]} is missing. {Scope}",
            var count when count == rules.Count =>
                $"The MM2 Lobby firewall rules are missing. {Scope}",
            _ => $"The MM2 Lobby firewall rules for {Join(missing)} are missing. {Scope}",
        };

        return new CheckResult(
            Name,
            CheckStatus.Fail,
            message,
            "Without these rules the firewall can block MM2, and then MM2 reports no host "
                + "and gives no reason. It can also block this app, and then the player list "
                + "stays empty. The fix adds the missing rules. "
                + "Windows will ask for permission up to three times. Choose Yes each time.");
    }

    public async Task<CheckResult> FixAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.GamePath))
        {
            return new CheckResult(
                Name,
                CheckStatus.Fail,
                "The game path is unknown, so the rule cannot name a program.",
                "Run the Midtown Madness 2 check first.");
        }

        if (string.IsNullOrWhiteSpace(_appPath))
        {
            return new CheckResult(
                Name,
                CheckStatus.Fail,
                "The app cannot read the path of its own program file, so the rule "
                    + "cannot name it.",
                "Report this to the person who runs the group. This computer needs the "
                    + "rules from Windows Defender Firewall by hand.");
        }

        // Each elevated process shows one permission prompt. The plain runner
        // needs no prompt, so the app asks it which rules exist. The app asks
        // about every rule before it starts the first elevated process. The app
        // changes a rule that exists with a set command. The app assumes that
        // a set command keeps the rule in place, so the rule is never missing
        // and a refused prompt cannot remove a rule that works. The Windows
        // script must check that assumption. The app adds a rule that does
        // not exist. The fix therefore shows three prompts in every case.
        var found = new List<(Rule Rule, bool Exists)>();

        foreach (var rule in TheRules()) found.Add((rule, await RuleExistsAsync(rule.Name, ct)));

        foreach (var (rule, exists) in found)
        {
            var arguments = exists
                ? $"advfirewall firewall set rule name=\"{rule.Name}\" dir={rule.Direction} "
                    + $"new program=\"{rule.Program}\""
                : $"advfirewall firewall add rule name=\"{rule.Name}\" dir={rule.Direction} "
                    + $"action=allow program=\"{rule.Program}\" enable=yes profile=any";

            var result = await _elevatedRunner.RunAsync("netsh.exe", arguments, ct);

            if (!result.Succeeded)
            {
                var verb = exists ? "update" : "add";

                return new CheckResult(
                    Name,
                    CheckStatus.Fail,
                    ElevatedCommand.FailureMessage(
                        $"netsh did not {verb} the {rule.Noun}", result.ExitCode),
                    ElevatedCommand.ManualFix("netsh " + arguments));
            }
        }

        var recheck = await RunAsync(ct);
        if (recheck.Status == CheckStatus.Pass) return recheck;

        // The commands worked, but the rules are still not listed. This text
        // must differ from the text that RunAsync returns. That text asks the
        // player to do what the player has just done.
        return new CheckResult(
            Name,
            CheckStatus.Fail,
            "Windows accepted the commands, but it does not list the MM2 Lobby firewall rules.",
            "Choose Check again. If the rules are still missing, report this to the person "
                + "who runs the group.");
    }

    private async Task<bool> RuleExistsAsync(string ruleName, CancellationToken ct)
    {
        var result = await _runner.RunAsync(
            "netsh.exe", $"advfirewall firewall show rule name=\"{ruleName}\"", ct);

        return result.Succeeded
            && result.StandardOutput.Contains(ruleName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gives every rule that this app owns, in the order of the commands. The
    /// method reads the game path at each call, because the player can choose
    /// another game folder while the app runs.
    /// </summary>
    private IReadOnlyList<Rule> TheRules() =>
    [
        new(InboundRuleName, "in", _config.GamePath, "inbound traffic to the game",
            "inbound rule"),
        new(OutboundRuleName, "out", _config.GamePath, "outbound traffic from the game",
            "outbound rule"),
        new(AppInboundRuleName, "in", _appPath, "inbound traffic to this app",
            "inbound rule of this app"),
    ];

    /// <summary>Names one part, or two parts joined by "and", or a longer list.</summary>
    private static string Join(IReadOnlyList<string> parts)
        => parts.Count <= 1
            ? string.Concat(parts)
            : $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}";

    /// <param name="Name">The name that Windows Defender Firewall shows.</param>
    /// <param name="Direction">The netsh word for the direction: in or out.</param>
    /// <param name="Program">The program file that the rule names.</param>
    /// <param name="Traffic">The traffic that the rule permits, for the player.</param>
    /// <param name="Noun">The name of the rule in a message about a command.</param>
    private readonly record struct Rule(
        string Name, string Direction, string Program, string Traffic, string Noun);
}
