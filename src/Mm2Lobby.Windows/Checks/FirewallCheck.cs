using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This check matters most. When the firewall blocks the game, MM2 fails
/// silently and reports only that it found no host. The check reads the game
/// path from the shared AppConfig when it adds the rules.
/// </summary>
/// <remarks>
/// The app keeps one rule for each direction, each with its own name. So the
/// check can see that only one direction is open. It does this without a
/// parse of the netsh output, which Windows translates into the language of
/// the user. The check cannot see that a rule names an old program path. The
/// fix deletes each rule and adds it again, so the fix repairs that case.
/// </remarks>
public sealed class FirewallCheck : ICheck
{
    public const string RuleName = "MM2 Lobby - Midtown Madness 2";
    public const string InboundRuleName = $"{RuleName} (inbound)";
    public const string OutboundRuleName = $"{RuleName} (outbound)";

    private const string NoHost =
        "Without it the game reports no host and gives no reason.";

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
            return new CheckResult(Name, CheckStatus.Pass, "Both rules are in place.", null);
        }

        var message = (inboundExists, outboundExists) switch
        {
            (true, false) => $"The outbound firewall rule for MM2 is missing. {NoHost}",
            (false, true) => $"The inbound firewall rule for MM2 is missing. {NoHost}",
            _ => $"No firewall rule allows MM2, inbound or outbound. {NoHost}",
        };

        return new CheckResult(
            Name,
            CheckStatus.Fail,
            message,
            "Add an inbound and an outbound allow rule. This needs administrator rights.");
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

        foreach (var (direction, ruleName) in new[]
        {
            ("in", InboundRuleName),
            ("out", OutboundRuleName),
        })
        {
            // The app deletes the old rule first. A rule that names an old
            // program path then cannot stay. Netsh returns a non-zero exit
            // code when no rule exists. That is not a failure, so the app
            // ignores the exit code of the delete.
            await _elevatedRunner.RunAsync(
                "netsh.exe", $"advfirewall firewall delete rule name=\"{ruleName}\"", ct);

            var arguments =
                $"advfirewall firewall add rule name=\"{ruleName}\" "
                + $"dir={direction} action=allow program=\"{path}\" enable=yes profile=any";

            var result = await _elevatedRunner.RunAsync("netsh.exe", arguments, ct);

            if (!result.Succeeded)
            {
                return new CheckResult(
                    Name,
                    CheckStatus.Fail,
                    $"netsh did not add the {direction} rule (exit code {result.ExitCode}). "
                        + "If you chose No at the administrator prompt, that is the cause.",
                    "Open an administrator command prompt and run: netsh " + arguments);
            }
        }

        return await RunAsync(ct);
    }

    private async Task<bool> RuleExistsAsync(string ruleName, CancellationToken ct)
    {
        var result = await _runner.RunAsync(
            "netsh.exe", $"advfirewall firewall show rule name=\"{ruleName}\"", ct);

        return result.Succeeded
            && result.StandardOutput.Contains(ruleName, StringComparison.Ordinal);
    }
}
