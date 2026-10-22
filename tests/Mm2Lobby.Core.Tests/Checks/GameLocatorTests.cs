using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

public class GameLocatorTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"mm2lobby-{Guid.NewGuid():N}");

    public GameLocatorTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void FindIn_returns_the_first_value_that_names_an_existing_game_file()
    {
        var first = CreateFile(Path.Combine("one", "midtown2.exe"));
        var second = CreateFile(Path.Combine("two", "midtown2.exe"));

        Assert.Equal(first, GameLocator.FindIn(new[] { first, second }));
    }

    [Fact]
    public void FindIn_skips_a_value_that_names_a_missing_file()
    {
        var missing = Path.Combine(_folder, "gone", "midtown2.exe");
        var present = CreateFile(Path.Combine("two", "midtown2.exe"));

        Assert.Equal(present, GameLocator.FindIn(new[] { missing, present }));
    }

    [Fact]
    public void FindIn_treats_a_value_as_a_folder_that_holds_the_game_file()
    {
        var game = CreateFile(Path.Combine("install", "midtown2.exe"));

        Assert.Equal(game, GameLocator.FindIn(new[] { Path.GetDirectoryName(game)! }));
    }

    [Fact]
    public void FindIn_ignores_a_value_that_names_another_existing_file()
    {
        var other = CreateFile(Path.Combine("install", "readme.txt"));

        Assert.Null(GameLocator.FindIn(new[] { other }));
    }

    [Fact]
    public void FindIn_returns_null_when_no_value_matches()
    {
        Assert.Null(GameLocator.FindIn(new[] { _folder, "not a path" }));
        Assert.Null(GameLocator.FindIn(Array.Empty<string>()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindIn_skips_a_blank_value_because_it_would_name_a_file_in_the_current_folder(
        string blank)
    {
        // A real File.Exists resolves the relative path "midtown2.exe" against
        // the current folder. This predicate says that such a file exists.
        var probed = new List<string>();
        bool Exists(string path)
        {
            probed.Add(path);
            return path == "midtown2.exe";
        }

        Assert.Null(GameLocator.FindIn(new[] { blank }, Exists));
        Assert.Empty(probed);
    }

    [Fact]
    public void IsTheGame_accepts_the_file_name_in_any_letter_case()
    {
        var game = CreateFile("MIDTOWN2.EXE");

        Assert.True(GameLocator.IsTheGame(game));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTheGame_rejects_a_blank_path(string? path)
    {
        Assert.False(GameLocator.IsTheGame(path));
    }

    [Fact]
    public void IsTheGame_rejects_a_game_path_that_does_not_exist()
    {
        Assert.False(GameLocator.IsTheGame(Path.Combine(_folder, "midtown2.exe")));
    }

    [Fact]
    public void IsTheGame_rejects_an_existing_file_with_another_name()
    {
        Assert.False(GameLocator.IsTheGame(CreateFile("notepad.exe")));
    }
}
