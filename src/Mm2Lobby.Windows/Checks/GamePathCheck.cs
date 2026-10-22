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
    private readonly ConfigStore _configStore;
    private readonly Func<string?> _readGamePathFromRegistry;
    private readonly Func<string?> _askTheUser;

    /// <param name="readGamePathFromRegistry">
    /// Returns the path of an existing midtown2.exe, or null. GameLocator.FindInRegistry
    /// is the real implementation.
    /// </param>
    /// <param name="askTheUser">
    /// Opens a file picker. It returns the path that the user chooses, or null
    /// when the user cancels. The window project supplies it.
    /// </param>
    public GamePathCheck(
        AppConfig config,
        ConfigStore configStore,
        Func<string?> readGamePathFromRegistry,
        Func<string?> askTheUser)
    {
        _config = config;
        _configStore = configStore;
        _readGamePathFromRegistry = readGamePathFromRegistry;
        _askTheUser = askTheUser;
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
            Save(found!);
            return Task.FromResult(Pass(found!));
        }

        return Task.FromResult(new CheckResult(
            Name,
            CheckStatus.Fail,
            $"The app cannot find {GameLocator.Executable}.",
            $"Choose {GameLocator.Executable} in the folder where you installed Midtown Madness 2."));
    }

    public Task<CheckResult> FixAsync(CancellationToken ct)
    {
        var chosen = _askTheUser();

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

        try
        {
            Save(chosen);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new CheckResult(
                Name,
                CheckStatus.Fail,
                $"The app found {GameLocator.Executable} but cannot save the path: {e.Message}",
                "Make sure the app can write to its settings folder. "
                    + $"Then choose {GameLocator.Executable} again."));
        }

        return Task.FromResult(Pass(chosen));
    }

    /// <summary>
    /// This method changes the shared configuration and saves it. If the save
    /// fails, the method restores the old path and throws the exception again.
    /// So a failed save leaves the memory and the file in the same state.
    /// </summary>
    private void Save(string path)
    {
        var previous = _config.GamePath;
        _config.GamePath = path;

        try
        {
            _configStore.Save(_config);
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
