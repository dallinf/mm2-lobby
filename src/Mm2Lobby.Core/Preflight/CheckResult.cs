namespace Mm2Lobby.Core.Preflight;

/// <param name="Name">The name of the check, which the window shows in the row.</param>
/// <param name="Status">The state of the check.</param>
/// <param name="Message">What the check found.</param>
/// <param name="FixDescription">What the Fix button does, or null.</param>
public sealed record CheckResult(
    string Name,
    CheckStatus Status,
    string Message,
    string? FixDescription)
{
    /// <summary>
    /// True when a result that does not pass must stop Host and Join.
    /// <para>
    /// The value is true for every check, and only an advisory check sets it
    /// to false. PreflightRunner copies ICheck.Blocks onto every result, so a
    /// check writes this answer one time and no return path can forget it.
    /// </para>
    /// </summary>
    public bool Blocks { get; init; } = true;
}
