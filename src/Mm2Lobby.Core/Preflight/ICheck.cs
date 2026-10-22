namespace Mm2Lobby.Core.Preflight;

public interface ICheck
{
    string Name { get; }

    Task<CheckResult> RunAsync(CancellationToken ct);

    /// <summary>Applies the fix. Then returns a new result for the check.</summary>
    Task<CheckResult> FixAsync(CancellationToken ct);
}
