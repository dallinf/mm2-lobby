namespace Mm2Lobby.Core.Tests.App;

/// <summary>
/// These tests read the project file of the app. The settings below belong to
/// a publish, and the tests run no publish, so no other test can see them. A
/// change to one of them breaks the app on the computer of a player and breaks
/// no test.
/// </summary>
public class PublishSettingsTests
{
    /// <summary>
    /// Reads the project file of the app. The method walks up from the folder
    /// of the test assembly until it meets the solution file.
    /// </summary>
    private static string ReadTheProjectFile()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Mm2Lobby.sln")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);

        var path = Path.Combine(folder.FullName, "src", "Mm2Lobby.App", "Mm2Lobby.App.csproj");
        Assert.True(File.Exists(path), $"The test looked for {path} and found no file.");

        return File.ReadAllText(path);
    }

    [Fact]
    public void The_app_publishes_as_32_bit_code()
    {
        // Windows 11 ships a 32 bit dplayx.dll only. A 64 bit process cannot
        // load DirectPlay, so LobbyLaunchPreconditions reports
        // WrongProcessBitness and the window starts MM2 the ordinary way on
        // every press. The player then types the address of the host, and the
        // one click start of this app never runs.
        Assert.Contains(
            "<RuntimeIdentifier>win-x86</RuntimeIdentifier>", ReadTheProjectFile());
    }

    [Fact]
    public void The_publish_gives_one_file_and_no_library_beside_it()
    {
        // Two reviews defended these three settings.
        // IncludeNativeLibrariesForSelfExtract does nothing without
        // PublishSingleFile, and a publish that leaves one of them out puts
        // the three native libraries of Avalonia beside the exe. A player who
        // then copies the exe alone sees the program fail to start.
        var text = ReadTheProjectFile();

        Assert.Contains("<PublishSingleFile>true</PublishSingleFile>", text);
        Assert.Contains(
            "<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>",
            text);
        Assert.Contains("<SelfContained>true</SelfContained>", text);
    }
}
