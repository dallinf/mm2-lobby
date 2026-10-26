using System.Text;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Diagnostics;

namespace Mm2Lobby.Core.Tests.Diagnostics;

public class ReportStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"mm2report-{Guid.NewGuid():N}");

    private string TheFile => Path.Combine(_folder, "report.txt");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Write_puts_the_text_in_the_file()
    {
        new ReportStore(TheFile).Write("the report");

        Assert.Equal("the report", File.ReadAllText(TheFile));
    }

    [Fact]
    public void Write_builds_the_folder_of_the_path()
    {
        // The app can write the report before the player ever saved a setting,
        // and then no component built the folder.
        new ReportStore(Path.Combine(_folder, "deeper", "report.txt")).Write("the report");

        Assert.True(File.Exists(Path.Combine(_folder, "deeper", "report.txt")));
    }

    [Fact]
    public void The_second_write_overwrites_the_one_file()
    {
        var store = new ReportStore(TheFile);

        store.Write("the first report");
        store.Write("the second report");

        // The count comes first. A read of the one file would throw for a
        // store that gave each write a name of its own, and the reader of a
        // red result must see the count that this test holds.
        Assert.Single(Directory.GetFiles(_folder));
        Assert.Equal("the second report", File.ReadAllText(TheFile));
    }

    [Fact]
    public void The_file_starts_with_the_mark_of_UTF_8()
    {
        // The report carries the dash that the lobby list shows for a peer
        // with no address. Notepad reads that character as two characters
        // while the file carries no mark.
        new ReportStore(TheFile).Write("a dash: —");

        Assert.Equal(
            Encoding.UTF8.GetPreamble(), File.ReadAllBytes(TheFile).Take(3).ToArray());
    }

    [Fact]
    public void The_default_path_sits_in_the_folder_of_the_configuration_file()
    {
        Assert.Equal(
            Path.GetDirectoryName(ConfigStore.DefaultPath),
            Path.GetDirectoryName(ReportStore.DefaultPath));

        Assert.Equal("report.txt", Path.GetFileName(ReportStore.DefaultPath));
    }
}
