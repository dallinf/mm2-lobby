using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// What came of one run of the command that changes the permission.
/// </summary>
/// <param name="Command">
/// The whole command that the app ran. The app shows this text to a player who
/// must run the command by hand.
/// </param>
/// <param name="Result">What Windows answered.</param>
public sealed record ZeroTierGrantRun(string Command, ProcessResult Result);
