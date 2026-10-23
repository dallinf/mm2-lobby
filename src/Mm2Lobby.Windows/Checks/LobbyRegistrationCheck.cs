using System.Text;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This check looks at the DirectPlay entry that this app owns. The lobby
/// launcher reads that entry, and it starts the game in a race only when the
/// entry is correct. A player who joins then types no address.
/// </summary>
/// <remarks>
/// Fourteen runs on Windows 11 found the three rules that the entry must obey.
/// The File value carries a leading backslash, because DirectPlay joins the
/// Path value and the File value with no separator. A Launcher value stops the
/// launch. Every player needs the same application GUID, because DirectPlay
/// joins only players who share one.
/// <para>
/// The key sits under HKEY_LOCAL_MACHINE, so every account on the computer
/// shares it and the fix needs administrator rights. The fix therefore writes
/// one script and runs one elevated command, and Windows shows one permission
/// prompt.
/// </para>
/// <para>
/// WARNING: this app owns one key, and it reads and writes that key only. An
/// installer of the game writes a key of its own, that key holds the
/// application GUID of the game, and a program such as GameRanger reads it. A
/// fix that wrote over that key would break the other program, and the player
/// would never connect the two.
/// </para>
/// </remarks>
public sealed class LobbyRegistrationCheck : ICheck
{
    /// <summary>The key that DirectPlay reads, under HKEY_LOCAL_MACHINE.</summary>
    public const string KeyPath = LobbyApplicationReader.KeyPath;

    /// <summary>
    /// The name of the key that this app owns. The app writes this key, and
    /// it reads this key. It changes no other key.
    /// </summary>
    public const string KeyName = LobbyApplicationReader.OwnKeyName;

    /// <summary>
    /// The application GUID of this group of players.
    /// <para>
    /// DirectPlay shows a player only the sessions that carry the same
    /// application GUID. The value does not have to be the GUID of the game.
    /// It has to be the same value on every computer of the group, and every
    /// player runs this app.
    /// </para>
    /// <para>
    /// A run on two computers on 2026-10-23 proved this value. One player
    /// hosted, the other player joined, and the player who joined typed no
    /// address.
    /// </para>
    /// <para>
    /// WARNING: do not change this value. Every player already holds it. A
    /// player who holds another value meets nobody, and MM2 then reports that
    /// it found no host and gives no reason.
    /// </para>
    /// </summary>
    public static readonly Guid ApplicationGuid =
        new("42418287-74D9-4C2D-A8D0-1B5D4D811C21");

    private const string Program = "reg.exe";

    private readonly AppConfig _config;
    private readonly IProcessRunner _elevatedRunner;
    private readonly Func<LobbyRegistration> _readRegistration;
    private readonly Func<string, string> _writeScript;

    /// <summary>The check that the app builds. It reads the real registry.</summary>
    /// <param name="config">The configuration that the window shares with the lobby.</param>
    /// <param name="elevatedRunner">Writes the key. It needs administrator rights.</param>
    public LobbyRegistrationCheck(AppConfig config, IProcessRunner elevatedRunner)
        : this(
            config,
            elevatedRunner,
            LobbyApplicationReader.ReadOwnFromThisComputer,
            WriteScriptToTempFile)
    {
    }

    /// <param name="config">The configuration that the window shares with the lobby.</param>
    /// <param name="elevatedRunner">Writes the key. It needs administrator rights.</param>
    /// <param name="readRegistration">Reads the key of this app on this computer.</param>
    /// <param name="writeScript">
    /// Writes the registry script to a file, and gives the path of that file.
    /// </param>
    internal LobbyRegistrationCheck(
        AppConfig config,
        IProcessRunner elevatedRunner,
        Func<LobbyRegistration> readRegistration,
        Func<string, string> writeScript)
    {
        _config = config;
        _elevatedRunner = elevatedRunner;
        _readRegistration = readRegistration;
        _writeScript = writeScript;
    }

    public string Name => "DirectPlay registration";

    /// <summary>
    /// This check gives advice. The app raced before this check existed: the
    /// window shows the address, the player types it into MM2, and the race
    /// happens. This key is machine wide, so a player on a work computer with
    /// no administrator rights cannot write it. A block would take the race
    /// away from that player, and the fix only buys the one click start.
    /// </summary>
    public bool Blocks => false;

    public Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var path = _config.GamePath;

        if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(NoGamePath());

        var registration = _readRegistration();

        var (message, extra) = registration.Fault switch
        {
            LobbyRegistrationFault.NoKey => (
                "Windows holds no DirectPlay entry for Midtown Madness 2. "
                    + "The app cannot start the game in a race for you.",
                ""),
            LobbyRegistrationFault.NoGuid => ("The DirectPlay entry holds no GUID.", ""),
            LobbyRegistrationFault.NoPath => ("The DirectPlay entry names no folder.", ""),
            LobbyRegistrationFault.NoFile => ("The DirectPlay entry names no file.", ""),
            LobbyRegistrationFault.FileWithoutLeadingBackslash => (
                "The File value of the DirectPlay entry starts with no backslash, "
                    + "so Windows looks for the wrong file.",
                ""),
            LobbyRegistrationFault.LauncherValuePresent => (
                "The DirectPlay entry holds a Launcher value, and that value stops the start.",
                " The fix also deletes the Launcher value."),
            _ => EntryIsOurs(registration, path),
        };

        if (message.Length == 0)
        {
            return Task.FromResult(new CheckResult(
                Name,
                CheckStatus.Pass,
                "Windows knows how to start Midtown Madness 2 in a race.",
                null));
        }

        return Task.FromResult(new CheckResult(Name, CheckStatus.Fail, message, Advice + extra));
    }

    public async Task<CheckResult> FixAsync(CancellationToken ct)
    {
        var path = _config.GamePath;

        if (string.IsNullOrWhiteSpace(path)) return NoGamePath();

        var split = SplitGamePath(path);

        if (split is null)
        {
            return new CheckResult(
                Name,
                CheckStatus.Fail,
                $"The game path {path} names no folder, so the entry cannot name one.",
                "Run the Midtown Madness 2 check again and choose midtown2.exe.");
        }

        // The fix writes the key of this app, with the GUID of the group. It
        // reads nothing of another key, and it writes nothing to another key.
        var script = RegistryScript(
            KeyName, ApplicationGuid, split.Value.Folder, split.Value.File);

        var scriptPath = _writeScript(script);
        var arguments = $"import \"{scriptPath}\"";
        var run = await _elevatedRunner.RunAsync(Program, arguments, ct);

        if (!run.Succeeded)
        {
            return new CheckResult(
                Name,
                CheckStatus.Fail,
                ElevatedCommand.FailureMessage(
                    "reg did not write the DirectPlay entry", run.ExitCode),
                ElevatedCommand.ManualFix($"reg {arguments}"));
        }

        var recheck = await RunAsync(ct);

        if (recheck.Status == CheckStatus.Pass)
        {
            return new CheckResult(
                Name,
                CheckStatus.Pass,
                "The app wrote the DirectPlay entry for Midtown Madness 2, "
                    + $"and it names {path}.",
                null);
        }

        // The command worked, and the registry still reports a fault. This
        // text must differ from the text of RunAsync, because that text asks
        // the player to do what the player has just done.
        return new CheckResult(
            Name,
            CheckStatus.Fail,
            "Windows accepted the command, and it still reports a problem with the entry.",
            "Choose Check again. If the row still reports a problem, report this to "
                + "the person who runs the group.");
    }

    /// <summary>
    /// This method builds the script that reg.exe imports. One import writes
    /// every value and deletes the Launcher value, so Windows shows one
    /// permission prompt.
    /// </summary>
    /// <param name="keyName">The name of the key under the Applications key.</param>
    /// <param name="guid">The application GUID that every player shares.</param>
    /// <param name="folder">The folder of the game, with no trailing backslash.</param>
    /// <param name="file">The file of the game, with a leading backslash.</param>
    internal static string RegistryScript(
        string keyName, Guid guid, string folder, string file)
    {
        var text = new StringBuilder();

        // A registry script takes the line ending of Windows, and reg.exe
        // reads the version line to know the format.
        text.Append("Windows Registry Editor Version 5.00\r\n\r\n");
        text.Append($@"[HKEY_LOCAL_MACHINE\{KeyPath}\{keyName}]").Append("\r\n");
        text.Append(Value("Guid", guid.ToString("B").ToUpperInvariant()));
        text.Append(Value("Path", folder));
        text.Append(Value("File", file));

        // MM2 needs no switch on its command line. The value must still exist,
        // because the entry that worked on Windows 11 held it.
        text.Append(Value("CommandLine", ""));
        text.Append(Value("CurrentDirectory", folder));

        // A minus sign deletes the value. A Launcher value stops the launch,
        // and a run on Windows 11 proved it.
        text.Append("\"Launcher\"=-\r\n");

        // reg.exe wants a blank line at the end of the file.
        text.Append("\r\n");

        return text.ToString();
    }

    /// <summary>
    /// This method splits a Windows game path into the folder and the file.
    /// The file keeps the leading backslash, because DirectPlay joins the two
    /// values with no separator.
    /// </summary>
    /// <remarks>
    /// This method does not call Path.GetDirectoryName. That method reads no
    /// backslash as a separator on macOS, so every test of the fix would read
    /// a path with no folder.
    /// </remarks>
    /// <param name="gamePath">The full path of midtown2.exe.</param>
    internal static (string Folder, string File)? SplitGamePath(string gamePath)
    {
        var cut = gamePath.LastIndexOf('\\');

        if (cut <= 0 || cut == gamePath.Length - 1) return null;

        return (gamePath[..cut], gamePath[cut..]);
    }

    /// <summary>
    /// This method reads the two things that the reader does not judge: the
    /// application GUID of the group, and the game of this computer. An empty
    /// message means that the entry is good.
    /// </summary>
    private static (string Message, string Extra) EntryIsOurs(
        LobbyRegistration registration, string gamePath)
    {
        var entry = registration.Entry!;

        // The app owns this key, so the key holds the GUID of the group. A
        // player who holds another value meets nobody, and MM2 gives no
        // reason for that.
        if (entry.ApplicationGuid != ApplicationGuid)
        {
            return (
                "The DirectPlay entry holds another application GUID, "
                    + "so you would meet none of your friends.",
                "");
        }

        var named = entry.GameFilePath;

        if (string.Equals(named, gamePath, StringComparison.OrdinalIgnoreCase))
        {
            return ("", "");
        }

        return ($"The DirectPlay entry names the file {named}. Your game is at {gamePath}.", "");
    }

    /// <summary>One value line of the script, with the backslashes doubled.</summary>
    private static string Value(string name, string text)
        => $"\"{name}\"=\"{Escape(text)}\"\r\n";

    /// <summary>
    /// A registry script carries a backslash as two backslashes, and a
    /// quotation mark after a backslash.
    /// </summary>
    private static string Escape(string text)
        => text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private CheckResult NoGamePath() => new(
        Name,
        CheckStatus.Fail,
        "The app does not know where Midtown Madness 2 is.",
        "Run the Midtown Madness 2 check first.");

    /// <summary>
    /// The text beside the Fix button. The warning stands first, because the
    /// fix changes a setting that every account on the computer shares.
    /// </summary>
    private const string Advice =
        "Warning: the fix writes a Windows setting that every account on this "
        + "computer shares. Windows asks for permission one time, and you choose Yes. "
        + "The fix then writes the DirectPlay entry for Midtown Madness 2, "
        + "and the app can start the game in a race for you. "
        + "This check is advice. The app does not stop the race for it. "
        + "Without the fix you can still race: start the game yourself, "
        + "then type the address of the host.";

    /// <summary>
    /// This method writes the script to a file for reg.exe. No test covers it,
    /// because a test drives the script text through the seam.
    /// </summary>
    private static string WriteScriptToTempFile(string script)
    {
        var path = Path.Combine(Path.GetTempPath(), "Mm2Lobby-registration.reg");

        // reg.exe reads a script in the ANSI code page or in UTF-16. The byte
        // order mark tells it which one this file uses.
        File.WriteAllText(path, script, new UnicodeEncoding(false, true));

        return path;
    }
}
