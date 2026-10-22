using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This check reads a file. It does not ask DISM, because dism /get-featureinfo
/// needs administrator rights and this check must run without them. The fix
/// needs those rights, so FixAsync uses an elevated runner.
/// </summary>
public sealed class DirectPlayCheck : ICheck
{
    private const string FeatureLibrary = @"C:\Windows\SysWOW64\dplayx.dll";

    private const string EnableCommand =
        "/online /enable-feature /featurename:DirectPlay /all /norestart";

    // DISM returns this code when the command worked and Windows needs a
    // restart. DISM often returns this code when the command has /norestart.
    private const int RestartRequired = 3010;

    private readonly IProcessRunner _elevatedRunner;
    private readonly Func<string, bool> _fileExists;

    public DirectPlayCheck(IProcessRunner elevatedRunner, Func<string, bool>? fileExists = null)
    {
        _elevatedRunner = elevatedRunner;
        _fileExists = fileExists ?? File.Exists;
    }

    public string Name => "DirectPlay";

    public Task<CheckResult> RunAsync(CancellationToken ct)
    {
        if (_fileExists(FeatureLibrary))
        {
            return Task.FromResult(new CheckResult(
                Name, CheckStatus.Pass, "The DirectPlay component is on.", null));
        }

        return Task.FromResult(new CheckResult(
            Name,
            CheckStatus.Fail,
            "Windows ships DirectPlay off, and MM2 needs it.",
            "Turn on the DirectPlay Windows component. "
                + "Windows will ask for permission one time. Choose Yes."));
    }

    public async Task<CheckResult> FixAsync(CancellationToken ct)
    {
        var result = await _elevatedRunner.RunAsync("dism.exe", EnableCommand, ct);

        if (result.ExitCode is not (0 or RestartRequired))
        {
            return new CheckResult(
                Name,
                CheckStatus.Fail,
                ElevatedCommand.FailureMessage("DISM did not finish", result.ExitCode),
                ElevatedCommand.ManualFix($"dism {EnableCommand}"));
        }

        var recheck = await RunAsync(ct);
        if (recheck.Status == CheckStatus.Pass) return recheck;

        // Exit code 3010 means that DISM worked and Windows needs a restart.
        // Only then does a restart help. Exit code 0 means that no restart is
        // pending. The file is then missing for another reason, and the check
        // may look for the file in the wrong place.
        if (result.ExitCode == RestartRequired)
        {
            return new CheckResult(
                Name,
                CheckStatus.Fail,
                "DISM finished, and Windows needs a restart to finish the work.",
                "Restart Windows, then run the check again.");
        }

        return new CheckResult(
            Name,
            CheckStatus.Fail,
            $"DISM reported success, but the app cannot find the DirectPlay file {FeatureLibrary}.",
            "Report this to the person who runs the group. Say which version of Windows you use.");
    }
}
