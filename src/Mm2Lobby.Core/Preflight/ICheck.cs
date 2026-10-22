namespace Mm2Lobby.Core.Preflight;

public interface ICheck
{
    string Name { get; }

    Task<CheckResult> RunAsync(CancellationToken ct);

    /// <summary>This method applies the fix. It then returns a new result for the check.</summary>
    Task<CheckResult> FixAsync(CancellationToken ct);
}
