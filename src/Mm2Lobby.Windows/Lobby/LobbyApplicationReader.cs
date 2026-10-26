using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Mm2Lobby.Windows.Lobby;

/// <summary>
/// One application key under the DirectPlay Applications key of the registry.
/// The reader takes this shape, so a test gives it a key and needs no registry.
/// </summary>
internal sealed class LobbyApplicationKey
{
    public LobbyApplicationKey(string name, IReadOnlyDictionary<string, string> values)
    {
        Name = name;

        // The registry ignores the letter case of a value name, so the reader
        // ignores it too.
        Values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The name of the key, which DirectPlay shows as the application name.</summary>
    public string Name { get; }

    /// <summary>Every string value of the key, by the name of the value.</summary>
    public IReadOnlyDictionary<string, string> Values { get; }
}

/// <summary>The fault that the reader found in the registration of the game.</summary>
internal enum LobbyRegistrationFault
{
    /// <summary>The registration is good.</summary>
    None,

    /// <summary>No key registers MM2.</summary>
    NoKey,

    /// <summary>The key holds no value that reads as a GUID.</summary>
    NoGuid,

    /// <summary>
    /// The key holds a GUID that is not the GUID of this group. DirectPlay
    /// shows a player only the sessions that carry the same application GUID,
    /// so that player would meet nobody.
    /// </summary>
    WrongGuid,

    /// <summary>The key holds no Path value.</summary>
    NoPath,

    /// <summary>The key holds no File value.</summary>
    NoFile,

    /// <summary>
    /// The File value starts with no backslash. DirectPlay joins Path and File
    /// with no separator, so the launch then looks for the wrong file.
    /// </summary>
    FileWithoutLeadingBackslash,

    /// <summary>
    /// The key holds a Launcher value. A run on Windows 11 proved that this
    /// value stops the launch.
    /// </summary>
    LauncherValuePresent,
}

/// <summary>
/// The registration of MM2 with the DirectPlay lobby, as the registry holds it.
/// </summary>
internal sealed record LobbyApplicationEntry(
    string KeyName,
    Guid ApplicationGuid,
    string Folder,
    string FileName,
    string? CommandLine,
    string? CurrentDirectory)
{
    /// <summary>
    /// The file that DirectPlay starts. DirectPlay joins the Path value and the
    /// File value with no separator, so the File value carries the leading
    /// backslash. A run on Windows 11 proved this rule.
    /// </summary>
    public string GameFilePath => Folder + FileName;
}

/// <summary>What the reader found, and the fault that it found.</summary>
/// <param name="Entry">The registration, or null when the reader found a fault.</param>
/// <param name="Fault">The fault that the reader found.</param>
internal sealed record LobbyRegistration(
    LobbyApplicationEntry? Entry,
    LobbyRegistrationFault Fault);

/// <summary>
/// This class reads the registration of MM2 with the DirectPlay lobby. The
/// launcher needs the application GUID, and the owner needs a report of a
/// registration that will not work.
/// <para>
/// ReadFrom takes the keys, so a test gives it keys and needs no registry. Only
/// ReadFromRegistry touches Windows.
/// </para>
/// </summary>
internal static class LobbyApplicationReader
{
    /// <summary>
    /// The key that holds the registration. MM2 is a 32 bit game, so the
    /// registration sits under WOW6432Node.
    /// </summary>
    public const string KeyPath =
        @"SOFTWARE\WOW6432Node\Microsoft\DirectPlay\Applications";

    /// <summary>
    /// The name of the key that this app writes. The firewall rules of the
    /// project carry the same name, so one name marks every thing that this
    /// app owns.
    /// <para>
    /// The app writes this key and reads this key. It never changes a key
    /// that another program wrote. An installer of the game writes a key of
    /// its own, that key holds the application GUID of the game, and another
    /// program such as GameRanger reads it.
    /// </para>
    /// </summary>
    public const string OwnKeyName = "MM2 Lobby - Midtown Madness 2";

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

    private const string GuidValue = "Guid";
    private const string PathValue = "Path";
    private const string FileValue = "File";
    private const string CommandLineValue = "CommandLine";
    private const string CurrentDirectoryValue = "CurrentDirectory";

    /// <summary>
    /// A Launcher value breaks the launch. A run on Windows 11 proved it, so
    /// the reader reports the value and the app offers to delete it.
    /// </summary>
    private const string LauncherValue = "Launcher";

    /// <summary>
    /// This method reads the key of this app, and it reads no other key. It
    /// returns the first fault that it finds.
    /// <para>
    /// WARNING: read no other key here, and add no fallback. The key of an
    /// installer holds the application GUID of the game, and this group
    /// shares another GUID. A launch against that key opens a session that
    /// looks correct and that no friend can see. MM2 reports nothing, and no
    /// player can report it either.
    /// </para>
    /// <para>
    /// A computer with no key of this app gives NoKey. The app then falls
    /// back to the ordinary launch, where the player types the address of the
    /// host. That is what every player has today, so nobody loses anything.
    /// </para>
    /// </summary>
    /// <param name="keys">Every key under the Applications key.</param>
    public static LobbyRegistration ReadFrom(IEnumerable<LobbyApplicationKey> keys)
    {
        // The registry ignores the letter case of a key name, so this method
        // ignores it too.
        var key = keys.FirstOrDefault(
            one => one.Name.Equals(OwnKeyName, StringComparison.OrdinalIgnoreCase));

        if (key is null) return new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

        if (!Guid.TryParse(Read(key, GuidValue), out var applicationGuid))
        {
            return new LobbyRegistration(null, LobbyRegistrationFault.NoGuid);
        }

        // The app owns this key, so the key holds the GUID of the group. A
        // player who holds another value meets nobody. The reader reports it,
        // so the setup check and the launcher cannot disagree.
        if (applicationGuid != ApplicationGuid)
        {
            return new LobbyRegistration(null, LobbyRegistrationFault.WrongGuid);
        }

        var folder = Read(key, PathValue);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return new LobbyRegistration(null, LobbyRegistrationFault.NoPath);
        }

        var file = Read(key, FileValue);
        if (string.IsNullOrWhiteSpace(file))
        {
            return new LobbyRegistration(null, LobbyRegistrationFault.NoFile);
        }

        var entry = new LobbyApplicationEntry(
            key.Name,
            applicationGuid,
            folder,
            file,
            Read(key, CommandLineValue),
            Read(key, CurrentDirectoryValue));

        if (!file.StartsWith('\\'))
        {
            return new LobbyRegistration(entry, LobbyRegistrationFault.FileWithoutLeadingBackslash);
        }

        if (key.Values.ContainsKey(LauncherValue))
        {
            return new LobbyRegistration(entry, LobbyRegistrationFault.LauncherValuePresent);
        }

        return new LobbyRegistration(entry, LobbyRegistrationFault.None);
    }

    /// <summary>
    /// This method reads the registration of this computer. It gives NoKey on
    /// a computer that does not run Windows, so the launcher and the setup
    /// check share one route to the registry.
    /// </summary>
    public static LobbyRegistration ReadFromThisComputer() => OperatingSystem.IsWindows()
        ? ReadFromRegistry()
        : new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

    /// <summary>
    /// This method reads the real registry. No test covers it, because a test
    /// cannot read a Windows registry on every computer. It holds the read and
    /// nothing else, and ReadFrom holds every decision.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static LobbyRegistration ReadFromRegistry() => ReadFrom(KeysFromRegistry());

    /// <summary>
    /// This method lists every key under the Applications key. No test covers
    /// it, for the same reason.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static List<LobbyApplicationKey> KeysFromRegistry()
    {
        var keys = new List<LobbyApplicationKey>();

        using var parent = Registry.LocalMachine.OpenSubKey(KeyPath);
        if (parent is null) return keys;

        foreach (var name in parent.GetSubKeyNames())
        {
            using var child = parent.OpenSubKey(name);
            if (child is null) continue;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var valueName in child.GetValueNames())
            {
                if (child.GetValue(valueName) is string text) values[valueName] = text;
            }

            keys.Add(new LobbyApplicationKey(name, values));
        }

        return keys;
    }

    private static string? Read(LobbyApplicationKey key, string name) =>
        key.Values.TryGetValue(name, out var value) ? value : null;
}
