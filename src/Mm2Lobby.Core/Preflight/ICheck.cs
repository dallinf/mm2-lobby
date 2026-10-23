namespace Mm2Lobby.Core.Preflight;

public interface ICheck
{
    string Name { get; }

    /// <summary>
    /// True when a failure of this check must stop Host and Join. Every check
    /// takes the default, and an advisory check writes
    /// <c>public bool Blocks =&gt; false;</c> with the reason beside it.
    /// <para>
    /// A check is advisory when the app works without it. The player then
    /// loses only the extra that the fix gives, and never the race itself. A
    /// player who cannot change the registry, because a work computer refuses
    /// administrator rights, keeps what they have today.
    /// </para>
    /// <para>
    /// The window still shows the row, the reason and the Fix button of an
    /// advisory check. Only PreflightRunner.AllPassed ignores it.
    /// </para>
    /// </summary>
    bool Blocks => true;

    Task<CheckResult> RunAsync(CancellationToken ct);

    /// <summary>This method applies the fix. It then returns a new result for the check.</summary>
    Task<CheckResult> FixAsync(CancellationToken ct);
}
