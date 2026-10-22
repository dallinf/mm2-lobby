using System.ComponentModel;
using System.Diagnostics;
using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Windows;

/// <summary>
/// This class runs a program and waits for it to exit. It never throws when
/// the program is missing or when the player refuses the permission
/// prompt. It returns NotRunExitCode instead. ZeroTierClient depends on this.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    /// <summary>
    /// The exit code for a program that did not run. The program is missing,
    /// or the player refused the permission prompt. ElevatedCommand and the
    /// test fakes use this constant, so each place has one source for the fact.
    /// </summary>
    public const int NotRunExitCode = 127;

    private readonly bool _elevate;

    public SystemProcessRunner(bool elevate = false) => _elevate = elevate;

    /// <summary>
    /// This method builds the program name and the argument text for one run.
    /// The CreateProcess function of Windows does not run a batch file. So a
    /// batch file goes to cmd.exe, and the name of the batch file becomes the
    /// first argument. The /s switch makes cmd.exe remove the first and the
    /// last quotation mark, and run the rest without a change. The path of
    /// the batch file can hold a space, so the path keeps its own pair of
    /// quotation marks.
    /// </summary>
    /// <remarks>
    /// This method reads the extension of the name only. It asks for no
    /// operating system, so a test can call it on macOS. A batch file has no
    /// meaning on macOS, and the locator finds no batch file there.
    /// </remarks>
    internal static (string FileName, string Arguments) BuildCommandLine(
        string fileName, string arguments)
    {
        var extension = Path.GetExtension(fileName);
        var isBatch = extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase);

        if (!isBatch) return (fileName, arguments);

        var inner = arguments.Length == 0
            ? $"\"{fileName}\""
            : $"\"{fileName}\" {arguments}";

        return ("cmd.exe", $"/s /c \"{inner}\"");
    }

    public async Task<ProcessResult> RunAsync(
        string fileName, string arguments, CancellationToken ct)
    {
        var line = BuildCommandLine(fileName, arguments);

        var info = new ProcessStartInfo(line.FileName, line.Arguments)
        {
            CreateNoWindow = true,
        };

        if (_elevate)
        {
            // The app cannot read the streams of an elevated process. The
            // caller therefore reads the exit code only.
            info.UseShellExecute = true;
            info.Verb = "runas";
        }
        else
        {
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null) return new ProcessResult(-1, "", "The process did not start.");

            var output = _elevate ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync(ct);
            var error = _elevate ? Task.FromResult("") : process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            return new ProcessResult(process.ExitCode, await output, await error);
        }
        catch (Exception e) when (e is Win32Exception or FileNotFoundException)
        {
            // Win32Exception covers a missing program. It also covers a player
            // who chooses No at the permission prompt.
            return new ProcessResult(NotRunExitCode, "", e.Message);
        }
    }
}
