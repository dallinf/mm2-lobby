namespace Mm2Lobby.Core.Preflight;

/// <summary>
/// Runs every check, even after one check fails. The player therefore sees the
/// whole list at the same time, not one problem for each attempt.
/// </summary>
public sealed class PreflightRunner
{
    private readonly IReadOnlyList<ICheck> _checks;

    public PreflightRunner(IReadOnlyList<ICheck> checks) => _checks = checks;

    public async Task<IReadOnlyList<CheckResult>> RunAllAsync(CancellationToken ct)
    {
        var results = new List<CheckResult>(_checks.Count);

        foreach (var check in _checks)
        {
            try
            {
                results.Add(await check.RunAsync(ct));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                results.Add(new CheckResult(
                    check.Name,
                    CheckStatus.Unavailable,
                    $"The check could not run: {e.Message}",
                    null));
            }
        }

        return results;
    }

    public static bool AllPassed(IReadOnlyList<CheckResult> results)
        => results.Count > 0 && results.All(result => result.Status == CheckStatus.Pass);
}
