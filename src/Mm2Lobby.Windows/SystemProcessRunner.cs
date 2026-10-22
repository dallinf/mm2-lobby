using System.ComponentModel;
using System.Diagnostics;
using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Windows;

/// <summary>
/// This class runs a program and waits for it to exit. It never throws when
/// the program is missing or when the player refuses the administrator
/// prompt. It returns exit code 127 instead. ZeroTierClient depends on this.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    private readonly bool _elevate;

    public SystemProcessRunner(bool elevate = false) => _elevate = elevate;

    public async Task<ProcessResult> RunAsync(
        string fileName, string arguments, CancellationToken ct)
    {
        var info = new ProcessStartInfo(fileName, arguments)
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
            // who chooses No at the administrator prompt.
            return new ProcessResult(127, "", e.Message);
        }
    }
}
