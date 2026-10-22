using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

public class GamePathCheckTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"mm2lobby-{Guid.NewGuid():N}");

    // The test and the check share this one object, as the window and its
    // checks do. A check must change this object and not a copy of it.
    private readonly AppConfig _config = new() { DisplayName = "Dallin" };

    private readonly ConfigStore _store;

    private string? _registryAnswer;
    private int _registryReads;
    private string? _pickerAnswer;
    private int _pickerOpens;

    public GamePathCheckTests()
    {
        Directory.CreateDirectory(_folder);
        _store = new ConfigStore(Path.Combine(_folder, "config.json"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private GamePathCheck Check() => new(
        _config,
        _store,
        () => { _registryReads++; return _registryAnswer; },
        () => { _pickerOpens++; return _pickerAnswer; });

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    private string ConfigFile => Path.Combine(_folder, "config.json");

    [Fact]
    public async Task Passes_with_the_saved_path_and_does_not_read_the_registry()
    {
        var game = CreateFile(Path.Combine("saved", "midtown2.exe"));
        _config.GamePath = game;

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Contains(game, result.Message);
        Assert.Equal(0, _registryReads);
        Assert.False(File.Exists(ConfigFile));
    }

    [Fact]
    public async Task Uses_the_registry_when_the_saved_path_names_a_file_that_is_gone()
    {
        var found = CreateFile(Path.Combine("registry", "midtown2.exe"));
        _config.GamePath = Path.Combine(_folder, "moved", "midtown2.exe");
        _registryAnswer = found;

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(found, _config.GamePath);
        Assert.Equal(1, _registryReads);
    }

    [Fact]
    public async Task Uses_the_registry_when_the_saved_path_names_another_existing_file()
    {
        var found = CreateFile(Path.Combine("registry", "midtown2.exe"));
        _config.GamePath = CreateFile("notepad.exe");
        _registryAnswer = found;

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(found, _config.GamePath);
    }

    [Fact]
    public async Task Saves_the_registry_path_so_the_next_start_reads_it_from_the_file()
    {
        _registryAnswer = CreateFile(Path.Combine("registry", "midtown2.exe"));

        await Check().RunAsync(default);

        Assert.Equal(_registryAnswer, _store.Load().GamePath);
    }

    [Fact]
    public async Task Keeps_the_game_path_when_another_component_saves_the_shared_configuration()
    {
        // The defect: the check changed a private copy, and the lobby then saved
        // its own copy and erased the path. The lobby does this on a timer.
        _registryAnswer = CreateFile(Path.Combine("registry", "midtown2.exe"));
        _config.RememberedNames["a09acf0233"] = "Sam";

        await Check().RunAsync(default);
        _config.RememberedNames["b19bd1344a"] = "Alex";
        _store.Save(_config);

        var reloaded = _store.Load();
        Assert.Equal(_registryAnswer, reloaded.GamePath);
        Assert.Equal("Dallin", reloaded.DisplayName);
        Assert.Equal(2, reloaded.RememberedNames.Count);
    }

    [Fact]
    public async Task Fails_and_writes_nothing_when_neither_source_has_the_game()
    {
        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("midtown2.exe", result.Message);
        Assert.NotNull(result.FixDescription);
        Assert.Equal(string.Empty, _config.GamePath);
        Assert.False(File.Exists(ConfigFile));
        Assert.Equal(0, _pickerOpens);
    }

    [Fact]
    public async Task Fails_when_the_registry_answers_with_a_path_that_does_not_exist()
    {
        // The real registry read returns only a file that exists. This answer
        // is one that the real read never gives, so the check must refuse it.
        _registryAnswer = Path.Combine(_folder, "ghost", "midtown2.exe");

        var result = await Check().RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(string.Empty, _config.GamePath);
        Assert.False(File.Exists(ConfigFile));
    }

    [Fact]
    public async Task FixAsync_saves_the_chosen_game_to_the_shared_configuration_and_the_file()
    {
        var game = CreateFile(Path.Combine("picked", "midtown2.exe"));
        _pickerAnswer = game;

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(game, _config.GamePath);
        Assert.Equal(game, _store.Load().GamePath);
        Assert.Equal(1, _pickerOpens);
        Assert.Equal(0, _registryReads);
    }

    [Fact]
    public async Task FixAsync_keeps_the_other_values_of_the_shared_configuration()
    {
        _config.RememberedNames["a09acf0233"] = "Sam";
        _pickerAnswer = CreateFile(Path.Combine("picked", "midtown2.exe"));

        await Check().FixAsync(default);

        var reloaded = _store.Load();
        Assert.Equal("Dallin", reloaded.DisplayName);
        Assert.Equal("Sam", reloaded.RememberedNames["a09acf0233"]);
    }

    [Fact]
    public async Task FixAsync_refuses_a_file_with_another_name_and_keeps_the_old_path()
    {
        var old = CreateFile(Path.Combine("old", "midtown2.exe"));
        _config.GamePath = old;
        _pickerAnswer = CreateFile("notepad.exe");

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("midtown2.exe", result.Message);
        Assert.Equal(old, _config.GamePath);
        Assert.False(File.Exists(ConfigFile));
    }

    [Fact]
    public async Task FixAsync_refuses_a_game_path_that_does_not_exist()
    {
        _pickerAnswer = Path.Combine(_folder, "ghost", "midtown2.exe");

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Equal(string.Empty, _config.GamePath);
        Assert.False(File.Exists(ConfigFile));
    }

    [Fact]
    public async Task FixAsync_says_that_no_file_was_chosen_when_the_picker_is_cancelled()
    {
        _pickerAnswer = null;

        var result = await Check().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("No file was chosen", result.Message);
        Assert.Equal(0, _registryReads);
        Assert.False(File.Exists(ConfigFile));
    }

    // A store whose folder is a file. ConfigStore.Save cannot create the folder
    // and throws an IOException, on every operating system.
    private GamePathCheck CheckWithUnwritableStore()
    {
        var blocker = CreateFile("blocker");
        var store = new ConfigStore(Path.Combine(blocker, "config.json"));
        return new GamePathCheck(
            _config, store, () => _registryAnswer, () => _pickerAnswer);
    }

    // A store whose file is a folder. ConfigStore.Save cannot move the new file
    // onto the folder. Windows reports this as UnauthorizedAccessException.
    private GamePathCheck CheckWithFolderInPlaceOfTheFile()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "config.json"));
        return new GamePathCheck(
            _config, _store, () => _registryAnswer, () => _pickerAnswer);
    }

    [Fact]
    public async Task FixAsync_reports_a_failed_fix_when_the_configuration_cannot_be_saved()
    {
        _pickerAnswer = CreateFile(Path.Combine("picked", "midtown2.exe"));

        var result = await CheckWithUnwritableStore().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("cannot save", result.Message);
        Assert.Matches(@"cannot save the path: \S", result.Message);
        Assert.Contains("settings folder", result.FixDescription);
    }

    [Fact]
    public async Task FixAsync_keeps_the_old_game_path_when_the_save_fails()
    {
        var old = CreateFile(Path.Combine("old", "midtown2.exe"));
        _config.GamePath = old;
        _pickerAnswer = CreateFile(Path.Combine("picked", "midtown2.exe"));

        await CheckWithUnwritableStore().FixAsync(default);

        Assert.Equal(old, _config.GamePath);
    }

    [Fact]
    public async Task FixAsync_reports_a_failed_fix_when_the_configuration_file_is_a_folder()
    {
        _pickerAnswer = CreateFile(Path.Combine("picked", "midtown2.exe"));

        var result = await CheckWithFolderInPlaceOfTheFile().FixAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("cannot save", result.Message);
    }

    [Fact]
    public async Task FixAsync_reports_a_failed_fix_when_the_settings_folder_is_read_only()
    {
        // ConfigStore.Save throws UnauthorizedAccessException when it cannot
        // write into the folder. A folder with no write permission shows this
        // on Unix. Windows ignores the Unix mode, and root ignores it too.
        // So the test does nothing on those systems.
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;

        var readOnly = Directory.CreateDirectory(Path.Combine(_folder, "readonly"));
        var store = new ConfigStore(Path.Combine(readOnly.FullName, "config.json"));
        var check = new GamePathCheck(_config, store, () => null,
            () => CreateFile(Path.Combine("picked", "midtown2.exe")));
        File.SetUnixFileMode(readOnly.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var result = await check.FixAsync(default);

            Assert.Equal(CheckStatus.Fail, result.Status);
            Assert.Contains("cannot save", result.Message);
            Assert.Equal(string.Empty, _config.GamePath);
        }
        finally
        {
            File.SetUnixFileMode(readOnly.FullName, UnixFileMode.UserRead
                | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task RunAsync_keeps_the_old_game_path_when_the_save_fails()
    {
        _registryAnswer = CreateFile(Path.Combine("registry", "midtown2.exe"));

        await Assert.ThrowsAnyAsync<IOException>(
            () => CheckWithUnwritableStore().RunAsync(default));

        Assert.Equal(string.Empty, _config.GamePath);
    }

    [Fact]
    public void Has_the_name_Midtown_Madness_2()
    {
        Assert.Equal("Midtown Madness 2", Check().Name);
    }
}
