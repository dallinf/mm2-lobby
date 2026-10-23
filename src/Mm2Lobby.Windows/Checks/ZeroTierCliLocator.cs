using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This class finds the ZeroTier command. The Windows installer of ZeroTier
/// does not add its own folder to the search list of Windows. So the plain
/// name zerotier-cli does not run on Windows, and the app must name the file
/// in full. This class looks in the install folder of ZeroTier first, and it
/// keeps the plain name as the last choice. macOS and Linux find the plain
/// name, so the last choice serves those two systems.
/// </summary>
/// <remarks>
/// The app calls Find one time, in LobbyEnvironment.CreateReal. The app reads
/// the file system one time for each run of the app, and not on every cycle
/// of the lobby.
/// </remarks>
public static class ZeroTierCliLocator
{
    /// <summary>
    /// The plain name. macOS and Linux find this name. ZeroTierClient holds
    /// the one source for this fact.
    /// </summary>
    public const string PlainCommand = ZeroTierClient.PlainCommand;

    /// <summary>The name of the file that the Windows installer writes.</summary>
    public const string WindowsCommandFile = "zerotier-cli.bat";

    /// <summary>
    /// The two variables of Windows that name a folder of programs. The
    /// Windows installer of ZeroTier writes to the 32-bit folder, so that
    /// folder comes first. A 32-bit Windows sets no ProgramFiles(x86)
    /// variable, so this class reads the second name too.
    /// </summary>
    private static readonly string[] ProgramFolderVariables =
        { "ProgramFiles(x86)", "ProgramFiles" };

    private const string VendorFolder = "ZeroTier";

    private const string ProductFolder = "One";

    /// <summary>
    /// This method returns the command file of ZeroTier, or the plain name
    /// when it finds no such file. It skips a blank variable, because
    /// Path.Combine would turn a blank value into a relative path in the
    /// current folder.
    /// </summary>
    /// <param name="fileExists">Says whether a file exists.</param>
    /// <param name="readVariable">Reads a variable of the environment.</param>
    public static ZeroTierCommand Find(
        Func<string, bool>? fileExists = null,
        Func<string, string?>? readVariable = null)
    {
        fileExists ??= File.Exists;
        readVariable ??= Environment.GetEnvironmentVariable;

        var folders = new List<string>();

        foreach (var name in ProgramFolderVariables)
        {
            var root = readVariable(name);
            if (string.IsNullOrWhiteSpace(root)) continue;

            var folder = Path.Combine(root, VendorFolder, ProductFolder);

            // On a 32-bit Windows both variables can name one folder.
            if (folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) continue;

            folders.Add(folder);

            var candidate = Path.Combine(folder, WindowsCommandFile);
            if (fileExists(candidate)) return new ZeroTierCommand(candidate, folders);
        }

        return new ZeroTierCommand(PlainCommand, folders);
    }
}
