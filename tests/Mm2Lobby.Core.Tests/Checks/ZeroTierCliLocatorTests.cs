using Mm2Lobby.Core.ZeroTier;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.Checks;

/// <summary>
/// These tests give the locator a predicate for the file system and a reader
/// for the two variables of Windows. So they run on macOS, and they need no
/// ZeroTier.
/// </summary>
public class ZeroTierCliLocatorTests
{
    private const string Wow = @"C:\Program Files (x86)";
    private const string Native = @"C:\Program Files";

    private readonly Dictionary<string, string?> _variables = new();
    private readonly List<string> _probed = new();
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    private static string CommandIn(string root)
        => Path.Combine(root, "ZeroTier", "One", "zerotier-cli.bat");

    private static string FolderIn(string root) => Path.Combine(root, "ZeroTier", "One");

    private bool Exists(string path)
    {
        _probed.Add(path);
        return _files.Contains(path);
    }

    private ZeroTierCommand Find()
        => ZeroTierCliLocator.Find(
            Exists, name => _variables.TryGetValue(name, out var value) ? value : null);

    [Fact]
    public void Find_returns_the_command_file_in_the_32_bit_program_folder()
    {
        _variables["ProgramFiles(x86)"] = Wow;
        _variables["ProgramFiles"] = Native;
        _files.Add(CommandIn(Wow));

        Assert.Equal(CommandIn(Wow), Find().FileName);
    }

    [Fact]
    public void Find_looks_in_the_other_program_folder_when_the_32_bit_variable_is_absent()
    {
        // A 32-bit Windows sets no ProgramFiles(x86) variable.
        _variables["ProgramFiles"] = Native;
        _files.Add(CommandIn(Native));

        Assert.Equal(CommandIn(Native), Find().FileName);
    }

    [Fact]
    public void Find_takes_the_32_bit_folder_first_when_both_folders_hold_the_command()
    {
        _variables["ProgramFiles(x86)"] = Wow;
        _variables["ProgramFiles"] = Native;
        _files.Add(CommandIn(Wow));
        _files.Add(CommandIn(Native));

        Assert.Equal(CommandIn(Wow), Find().FileName);
    }

    [Fact]
    public void Find_keeps_the_plain_name_when_it_finds_no_command_file()
    {
        // macOS and Linux keep zerotier-cli where the shell finds it. So the
        // plain name must stay the last choice.
        _variables["ProgramFiles(x86)"] = Wow;
        _variables["ProgramFiles"] = Native;

        Assert.Equal("zerotier-cli", Find().FileName);
    }

    [Fact]
    public void Find_names_every_folder_that_it_looked_in()
    {
        _variables["ProgramFiles(x86)"] = Wow;
        _variables["ProgramFiles"] = Native;

        Assert.Equal(
            new[] { FolderIn(Wow), FolderIn(Native) },
            Find().SearchedFolders);
    }

    [Fact]
    public void Find_names_no_folder_when_the_computer_sets_no_program_folder_variable()
    {
        // macOS and Linux set neither variable.
        var command = Find();

        Assert.Equal("zerotier-cli", command.FileName);
        Assert.Empty(command.SearchedFolders);
        Assert.Empty(_probed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_skips_a_blank_variable(string blank)
    {
        // Path.Combine turns a blank root into a relative path. So the locator
        // must not probe such a path, and must not name such a folder.
        _variables["ProgramFiles(x86)"] = blank;

        var command = Find();

        Assert.Equal("zerotier-cli", command.FileName);
        Assert.Empty(command.SearchedFolders);
        Assert.Empty(_probed);
    }

    [Fact]
    public void Find_looks_in_one_folder_one_time_when_both_variables_name_it()
    {
        _variables["ProgramFiles(x86)"] = Native;
        _variables["ProgramFiles"] = Native;

        var command = Find();

        Assert.Equal(new[] { FolderIn(Native) }, command.SearchedFolders);
        Assert.Single(_probed);
    }

    [Fact]
    public void Find_looks_for_the_command_file_under_the_ZeroTier_One_folder()
    {
        _variables["ProgramFiles(x86)"] = Wow;

        Find();

        Assert.Equal(new[] { CommandIn(Wow) }, _probed);
    }

    [Fact]
    public void The_class_names_the_plain_command_and_the_Windows_command_file()
    {
        Assert.Equal("zerotier-cli", ZeroTierCliLocator.PlainCommand);
        Assert.Equal("zerotier-cli.bat", ZeroTierCliLocator.WindowsCommandFile);

        // Two places use the plain name: the locator, and the default of
        // ZeroTierClient. One constant holds the fact.
        Assert.Equal(ZeroTierClient.PlainCommand, ZeroTierCliLocator.PlainCommand);
    }
}
