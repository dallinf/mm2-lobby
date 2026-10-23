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
/// <param name="KeyName">
/// The name of the key that holds the word midtown, or null when no key holds
/// it. The reader reports this name beside a fault that gives no entry, so the
/// setup check can mend the key that the player already has. A fix that wrote
/// a second key with that word would leave the reader a choice between two
/// keys.
/// </param>
internal sealed record LobbyRegistration(
    LobbyApplicationEntry? Entry,
    LobbyRegistrationFault Fault,
    string? KeyName = null);

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
    /// This method chooses the key of MM2 and reads its values. It returns the
    /// first fault that it finds.
    /// </summary>
    public static LobbyRegistration ReadFrom(IEnumerable<LobbyApplicationKey> keys)
    {
        var key = keys.FirstOrDefault(
            one => one.Name.Contains(KeyNameMark, StringComparison.OrdinalIgnoreCase));

        if (key is null) return new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

        if (!Guid.TryParse(Read(key, GuidValue), out var applicationGuid))
        {
            return new LobbyRegistration(null, LobbyRegistrationFault.NoGuid, key.Name);
        }

        var folder = Read(key, PathValue);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return new LobbyRegistration(null, LobbyRegistrationFault.NoPath, key.Name);
        }

        var file = Read(key, FileValue);
        if (string.IsNullOrWhiteSpace(file))
        {
            return new LobbyRegistration(null, LobbyRegistrationFault.NoFile, key.Name);
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
            return new LobbyRegistration(
                entry, LobbyRegistrationFault.FileWithoutLeadingBackslash, key.Name);
        }

        if (key.Values.ContainsKey(LauncherValue))
        {
            return new LobbyRegistration(
                entry, LobbyRegistrationFault.LauncherValuePresent, key.Name);
        }

        return new LobbyRegistration(entry, LobbyRegistrationFault.None, key.Name);
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
    public static LobbyRegistration ReadFromRegistry()
    {
        using var parent = Registry.LocalMachine.OpenSubKey(KeyPath);
        if (parent is null) return new LobbyRegistration(null, LobbyRegistrationFault.NoKey);

        var keys = new List<LobbyApplicationKey>();

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

        return ReadFrom(keys);
    }

    private static string? Read(LobbyApplicationKey key, string name) =>
        key.Values.TryGetValue(name, out var value) ? value : null;
}
