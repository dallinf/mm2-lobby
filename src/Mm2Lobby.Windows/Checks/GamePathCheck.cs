using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This check finds midtown2.exe. It tries the saved path first. Then it tries
/// the registry. The check changes the AppConfig that the window shares with
/// the lobby, and it saves through the shared ConfigStore. A check that loads
/// its own copy would lose the game path when the lobby saves its older copy.
/// </summary>
public sealed class GamePathCheck : ICheck
{
    private readonly AppConfig _config;
    private readonly Action<AppConfig> _save;
    private readonly Func<string?> _readGamePathFromRegistry;
    private readonly Func<string?> _askThePlayer;

    /// <param name="readGamePathFromRegistry">
    /// Returns the path of an existing midtown2.exe, or null. GameLocator.FindInRegistry
    /// is the real implementation.
    /// </param>
    /// <param name="askThePlayer">
    /// Opens a file picker. It returns the path that the player chooses, or null
    /// when the player cancels. The window project supplies it.
    /// </param>
    public GamePathCheck(
        AppConfig config,
        ConfigStore configStore,
        Func<string?> readGamePathFromRegistry,
        Func<string?> askThePlayer)
        : this(config, configStore.Save, readGamePathFromRegistry, askThePlayer)
    {
    }

    /// <summary>
    /// This constructor lets a test replace the Save method of ConfigStore.
    /// ConfigStore is a sealed class, so a test cannot make it throw every
    /// kind of exception.
    /// </summary>
    internal GamePathCheck(
        AppConfig config,
        Action<AppConfig> save,
        Func<string?> readGamePathFromRegistry,
        Func<string?> askThePlayer)
    {
        _config = config;
        _save = save;
        _readGamePathFromRegistry = readGamePathFromRegistry;
        _askThePlayer = askThePlayer;
    }

    public string Name => "Midtown Madness 2";

    public Task<CheckResult> RunAsync(CancellationToken ct)
    {
        if (GameLocator.IsTheGame(_config.GamePath))
        {
            return Task.FromResult(Pass(_config.GamePath));
        }

        // The check does not trust the answer. The real read gives only a path
        // that exists, and the check must refuse any other answer.
        var found = _readGamePathFromRegistry();
        if (GameLocator.IsTheGame(found))
        {
            var failure = TrySave(found!);
            return Task.FromResult(failure ?? Pass(found!));
        }

        return Task.FromResult(new CheckResult(
            Name,
            CheckStatus.Fail,
            $"The app cannot find {GameLocator.Executable}.",
            $"Choose {GameLocator.Executable} in the folder where you installed Midtown Madness 2."));
    }

    public Task<CheckResult> FixAsync(CancellationToken ct)
    {
        var chosen = _askThePlayer();

        if (string.IsNullOrWhiteSpace(chosen))
        {
            return Task.FromResult(new CheckResult(
                Name,
                CheckStatus.Fail,
                "No file was chosen.",
                $"Choose {GameLocator.Executable} in the folder where you installed Midtown Madness 2."));
        }

        if (!GameLocator.IsTheGame(chosen))
        {
            return Task.FromResult(new CheckResult(
                Name,
                CheckStatus.Fail,
                $"That file is not {GameLocator.Executable}.",
                $"Choose {GameLocator.Executable} in the folder where you installed Midtown Madness 2."));
        }

        return Task.FromResult(TrySave(chosen) ?? Pass(chosen));
    }

    /// <summary>
    /// This method calls Save. It returns null when Save works. It returns a
    /// failed result when Save throws an IOException or an
    /// UnauthorizedAccessException.
    /// </summary>
    private CheckResult? TrySave(string path)
    {
        try
        {
            Save(path);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new CheckResult(
                Name,
                CheckStatus.Fail,
                $"The app found {GameLocator.Executable} but cannot save the path: {e.Message}",
                "Make sure the app can write to its settings folder. "
                    + $"Then choose {GameLocator.Executable} again.");
        }
    }

    /// <summary>
    /// This method changes the shared configuration and saves it. If the
    /// method that saves throws an exception, this method restores the old path
    /// and throws the same exception again. So the memory and the file keep the
    /// same path.
    /// </summary>
    private void Save(string path)
    {
        var previous = _config.GamePath;
        _config.GamePath = path;

        try
        {
            _save(_config);
        }
        catch
        {
            _config.GamePath = previous;
            throw;
        }
    }

    private CheckResult Pass(string path)
        => new(Name, CheckStatus.Pass, $"Found at {path}.", null);
}
