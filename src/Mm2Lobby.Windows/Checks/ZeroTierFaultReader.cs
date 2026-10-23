using Mm2Lobby.Core.Processes;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This class reads the reason a ZeroTier command gave no answer. ZeroTierCheck
/// calls it only after ZeroTierClient answered null, because ZeroTierClient
/// hides the reason of a failure by its design.
/// </summary>
/// <remarks>
/// This class runs the info command one more time. The player starts the
/// checks, and the app runs this class on the failure path only. So this
/// class adds no work to the two-second cycle of the lobby.
/// </remarks>
public sealed class ZeroTierFaultReader
{
    /// <summary>
    /// The exit code that zerotier-cli gives when it cannot read its own
    /// authentication token.
    /// </summary>
    public const int TokenExitCode = 2;

    /// <summary>The name of the file that holds the token.</summary>
    private const string TokenFile = "authtoken.secret";

    private readonly IProcessRunner _runner;

    public ZeroTierFaultReader(IProcessRunner runner, ZeroTierCommand command)
    {
        _runner = runner;
        Command = command;
    }

    /// <summary>The command that the locator found.</summary>
    public ZeroTierCommand Command { get; }

    public async Task<ZeroTierFault> ReadAsync(CancellationToken ct)
    {
        var result = await _runner.RunAsync(
            Command.FileName, ZeroTierArguments.For(ZeroTierArguments.Info), ct);

        // The runner gives NotRunExitCode when the program is not there. A
        // name that Windows cannot resolve gives the same code.
        if (result.ExitCode == SystemProcessRunner.NotRunExitCode)
        {
            return ZeroTierFault.CommandNotFound;
        }

        // zerotier-cli gives exit code 2 and names the file of the token when
        // it cannot read that token. The app cannot know which of the two
        // streams carries the text, so it reads both.
        if (result.ExitCode == TokenExitCode && NamesTheTokenFile(result))
        {
            return ZeroTierFault.PermissionDenied;
        }

        return ZeroTierFault.Unknown;
    }

    private static bool NamesTheTokenFile(ProcessResult result)
        => result.StandardOutput.Contains(TokenFile, StringComparison.OrdinalIgnoreCase)
            || result.StandardError.Contains(TokenFile, StringComparison.OrdinalIgnoreCase);
}
