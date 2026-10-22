namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This class writes the message for an elevated command that failed.
/// SystemProcessRunner returns NotRunExitCode when the program is missing and
/// when the player refuses the permission prompt. So only that code can blame
/// the prompt. Any other code means that the command ran and failed.
/// </summary>
internal static class ElevatedCommand
{
    /// <param name="whatFailed">A sentence part, for example "netsh did not add the inbound rule".</param>
    public static string FailureMessage(string whatFailed, int exitCode)
        => exitCode == SystemProcessRunner.NotRunExitCode
            ? $"{whatFailed} (exit code {exitCode}). Windows did not run the command. "
                + "If you chose No at the permission prompt, that is the cause."
            : $"{whatFailed} (exit code {exitCode}). The command failed.";

    /// <param name="command">The program and its arguments, for example "netsh advfirewall ...".</param>
    public static string ManualFix(string command)
        => "Press Fix again and choose Yes at each permission prompt. "
            + $"Or open an administrator command prompt and run: {command}";
}
