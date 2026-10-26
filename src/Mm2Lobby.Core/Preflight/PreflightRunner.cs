namespace Mm2Lobby.Core.Preflight;

/// <summary>
/// This class runs every check, even after one check fails. The player therefore sees the
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
                // The check owns the answer to Blocks, and the runner copies
                // that answer onto the result. A check that builds many
                // results therefore cannot forget it on one path.
                var result = await check.RunAsync(ct);
                results.Add(result with { Blocks = check.Blocks });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                results.Add(new CheckResult(
                    check.Name,
                    CheckStatus.Unavailable,
                    $"The check could not run: {e.Message}",
                    null)
                {
                    Blocks = check.Blocks,
                });
            }
        }

        return results;
    }

    /// <summary>
    /// True when every check that blocks the lobby passed. The window reads
    /// this answer to enable Host and Join.
    /// <para>
    /// An advisory result takes no part here. The app works without an
    /// advisory check, so a failure of one costs the player nothing that they
    /// have today. The window still shows the row and the Fix button.
    /// </para>
    /// </summary>
    public static bool AllPassed(IReadOnlyList<CheckResult> results)
    {
        // A list with no blocking result says as little as an empty list. The
        // window must not start the lobby on it.
        var blocking = results.Where(result => result.Blocks).ToList();

        return blocking.Count > 0 && blocking.All(result => result.Status == CheckStatus.Pass);
    }
}
