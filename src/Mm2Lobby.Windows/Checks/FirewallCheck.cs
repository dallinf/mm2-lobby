using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This check matters most. When the firewall blocks the game, MM2 fails
/// silently and reports only that it found no host. The check reads the game
/// path from the shared AppConfig when it adds the rules.
/// </summary>
public sealed class FirewallCheck : ICheck
{
    public const string RuleName = "MM2 Lobby - Midtown Madness 2";

    private readonly IProcessRunner _runner;
    private readonly IProcessRunner _elevatedRunner;
    private readonly AppConfig _config;

    /// <param name="runner">Reads the rules. It needs no administrator rights.</param>
    /// <param name="elevatedRunner">Adds the rules. It needs administrator rights.</param>
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
        var result = await _runner.RunAsync(
            "netsh.exe", $"advfirewall firewall show rule name=\"{RuleName}\"", ct);

        if (result.Succeeded && result.StandardOutput.Contains(RuleName, StringComparison.Ordinal))
        {
            return new CheckResult(Name, CheckStatus.Pass, "The rule is in place.", null);
        }

        return new CheckResult(
            Name,
            CheckStatus.Fail,
            "No firewall rule allows MM2. Without it the game reports no host and gives no reason.",
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

        foreach (var direction in new[] { "in", "out" })
        {
            var arguments =
                $"advfirewall firewall add rule name=\"{RuleName}\" "
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
}
