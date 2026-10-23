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

    /// <summary>The word that names the key of MM2.</summary>
    public const string KeyNameMark = "midtown";

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
    /// This method chooses the key that the launcher reads, and it reads the
    /// values of that key.
    /// <para>
    /// The key of this app comes first. Its name also holds the word midtown,
    /// so a match on that word alone could choose the key of an installer
    /// instead. That key holds the application GUID of the game, and the
    /// group shares another GUID. The players would then meet nobody.
    /// </para>
    /// <para>
    /// A computer with no key of this app keeps the key that it has. The
    /// computer of the owner carries such a key from the spike, and fourteen
    /// runs worked against it.
    /// </para>
    /// </summary>
    public static LobbyRegistration ReadFrom(IEnumerable<LobbyApplicationKey> keys)
    {
        var all = keys.ToList();

        var key = Named(all, OwnKeyName)
            ?? all.FirstOrDefault(
                one => one.Name.Contains(KeyNameMark, StringComparison.OrdinalIgnoreCase));

        return ValuesOf(key);
    }

    /// <summary>
    /// This method reads one key by its name, and it reads no other key. The
    /// setup check owns one key, so it judges that key alone.
    /// </summary>
    /// <param name="keys">Every key under the Applications key.</param>
    /// <param name="name">The name of the key to read.</param>
    public static LobbyRegistration ReadNamed(
        IEnumerable<LobbyApplicationKey> keys, string name)
        => ValuesOf(Named(keys.ToList(), name));

    /// <summary>
    /// This method reads the values of one key. It returns the first fault
    /// that it finds.
    /// </summary>
    /// <param name="key">The key, or null when the reader found none.</param>
    private static LobbyRegistration ValuesOf(LobbyApplicationKey? key)
    {
        if (key is null) return new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

        if (!Guid.TryParse(Read(key, GuidValue), out var applicationGuid))
        {
            return new LobbyRegistration(null, LobbyRegistrationFault.NoGuid);
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
    /// This method finds one key by name. The registry ignores the letter
    /// case of a key name, so this method ignores it too.
    /// </summary>
    private static LobbyApplicationKey? Named(IReadOnlyList<LobbyApplicationKey> keys, string name)
        => keys.FirstOrDefault(
            one => one.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// This method reads the registration of this computer. It gives NoKey on
    /// a computer that does not run Windows, so the launcher and the setup
    /// check share one route to the registry.
    /// </summary>
    public static LobbyRegistration ReadFromThisComputer() => OperatingSystem.IsWindows()
        ? ReadFromRegistry()
        : new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

    /// <summary>
    /// This method reads the key of this app on this computer. It gives NoKey
    /// on a computer that does not run Windows.
    /// </summary>
    public static LobbyRegistration ReadOwnFromThisComputer() => OperatingSystem.IsWindows()
        ? ReadNamed(KeysFromRegistry(), OwnKeyName)
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
