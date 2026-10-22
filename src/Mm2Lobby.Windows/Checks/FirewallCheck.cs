using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This check looks for the two firewall rules that this app adds. When the
/// firewall blocks the game, MM2 fails silently and reports only that it found
/// no host. The check reads the game path from the shared AppConfig when the
/// fix writes the rules.
/// </summary>
/// <remarks>
/// The app keeps one rule for each direction, and each rule has its own name.
/// So the check can see that only one direction is missing. The check does not
/// read the text of the netsh output, because Windows translates that text.
/// The check sees only these two rules. A rule that Windows made when the
/// player chose Allow at the firewall prompt is not visible to the check. The check
/// also cannot see that a rule names an old program path. The fix changes each
/// rule that exists, so the fix repairs that case.
/// </remarks>
public sealed class FirewallCheck : ICheck
{
    public const string RuleName = "MM2 Lobby - Midtown Madness 2";
    public const string InboundRuleName = $"{RuleName} (inbound)";
    public const string OutboundRuleName = $"{RuleName} (outbound)";

    // The check cannot see a rule that Windows made. The text says so, and it
    // says what the Fix button does in that case. The window enables Host and
    // Join only when every check passes, so the text must not offer a skip.
    private const string Scope =
        "The check sees only the two rules that this app adds. If Windows already has "
        + "an Allow rule for MM2, pressing Fix adds the app's own rules.";

    private readonly IProcessRunner _runner;
    private readonly IProcessRunner _elevatedRunner;
    private readonly AppConfig _config;

    /// <param name="runner">Reads the rules. It needs no administrator rights.</param>
    /// <param name="elevatedRunner">Changes the rules. It needs administrator rights.</param>
    /// <param name="config">The configuration that the window shares with the lobby.</param>
    public FirewallCheck(
        IProcessRunner runner, IProcessRunner elevatedRunner, AppConfig config)
    {
        _runner = runner;
        _elevatedRunner = elevatedRunner;
        _config = config;
    }

    public string Name => "Windows Firewall";

    public async Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var inboundExists = await RuleExistsAsync(InboundRuleName, ct);
        var outboundExists = await RuleExistsAsync(OutboundRuleName, ct);

        if (inboundExists && outboundExists)
        {
            return new CheckResult(
                Name, CheckStatus.Pass, "The MM2 Lobby firewall rules are in place.", null);
        }

        var message = (inboundExists, outboundExists) switch
        {
            (true, false) =>
                $"The MM2 Lobby firewall rule for outbound traffic is missing. {Scope}",
            (false, true) =>
                $"The MM2 Lobby firewall rule for inbound traffic is missing. {Scope}",
            _ => $"The MM2 Lobby firewall rules are missing. {Scope}",
        };

        return new CheckResult(
            Name,
            CheckStatus.Fail,
            message,
            "Without these rules the firewall can block MM2, and then MM2 reports no host "
                + "and gives no reason. The fix adds the missing rules. "
                + "Windows will ask for permission up to two times. Choose Yes each time.");
    }

    public async Task<CheckResult> FixAsync(CancellationToken ct)
    {
        var path = _config.GamePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            return new CheckResult(
                Name,
                CheckStatus.Fail,
                "The game path is unknown, so the rule cannot name a program.",
                "Run the Midtown Madness 2 check first.");
        }

        // Each elevated process shows one permission prompt. The plain runner
        // needs no prompt, so the app asks it which rules exist. The app
        // changes a rule that exists with a set command. The app assumes that
        // a set command keeps the rule in place, so the rule is never missing
        // and a refused prompt cannot remove a rule that works. The Windows
        // script must check that assumption. The app adds a rule that does
        // not exist. The fix therefore shows two prompts in every case.
        var inboundExists = await RuleExistsAsync(InboundRuleName, ct);
        var outboundExists = await RuleExistsAsync(OutboundRuleName, ct);

        foreach (var (direction, word, ruleName, exists) in new[]
        {
            ("in", "inbound", InboundRuleName, inboundExists),
            ("out", "outbound", OutboundRuleName, outboundExists),
        })
        {
            var arguments = exists
                ? $"advfirewall firewall set rule name=\"{ruleName}\" dir={direction} "
                    + $"new program=\"{path}\""
                : $"advfirewall firewall add rule name=\"{ruleName}\" "
                    + $"dir={direction} action=allow program=\"{path}\" enable=yes profile=any";

            var result = await _elevatedRunner.RunAsync("netsh.exe", arguments, ct);

            if (!result.Succeeded)
            {
                var verb = exists ? "update" : "add";

                return new CheckResult(
                    Name,
                    CheckStatus.Fail,
                    ElevatedCommand.FailureMessage(
                        $"netsh did not {verb} the {word} rule", result.ExitCode),
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
}
