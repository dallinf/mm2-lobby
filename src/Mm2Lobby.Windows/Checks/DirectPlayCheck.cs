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
    // restart. With /norestart, DISM returns this code often.
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
            "Turn on the DirectPlay Windows component. This needs administrator rights."));
    }

    public async Task<CheckResult> FixAsync(CancellationToken ct)
    {
        var result = await _elevatedRunner.RunAsync("dism.exe", EnableCommand, ct);

        if (result.ExitCode is not (0 or RestartRequired))
        {
            return new CheckResult(
                Name,
                CheckStatus.Fail,
                $"DISM did not finish (exit code {result.ExitCode}). "
                    + "If you chose No at the administrator prompt, that is the cause.",
                "Open an administrator command prompt and run: "
                    + $"dism {EnableCommand}");
        }

        var recheck = await RunAsync(ct);
        if (recheck.Status == CheckStatus.Pass) return recheck;

        // DISM finished, but Windows has not put the file in place. Windows
        // does this until the next restart.
        return new CheckResult(
            Name,
            CheckStatus.Fail,
            "DISM finished, but Windows has not put the DirectPlay files in place yet.",
            "Restart Windows, then run the check again.");
    }
}
