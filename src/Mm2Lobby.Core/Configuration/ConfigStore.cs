using System.Collections.Concurrent;
using System.Text.Json;

namespace Mm2Lobby.Core.Configuration;

/// <summary>
/// This class reads and writes the one configuration file. If the file is
/// missing or malformed, Load returns the default configuration. A malformed
/// file must not stop the app.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;

    /// <summary>The lock that Save holds. See the remarks on that method.</summary>
    private readonly object _gate = new();

    public ConfigStore(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Mm2Lobby",
        "config.json");

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_path)) return Defaults();

            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), Json);
            if (config is null) return Defaults();

            if (string.IsNullOrWhiteSpace(config.DisplayName))
            {
                config.DisplayName = Environment.MachineName;
            }

            // A file can hold "gamePath":null. That value parses to a null
            // here. Neither property accepts a null.
            config.GamePath ??= string.Empty;
            config.ZeroTierNetworkId ??= string.Empty;

            config.RememberedNames = CopyValidEntries(config.RememberedNames);

            return config;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return Defaults();
        }
    }

    /// <summary>
    /// This method writes the configuration. It throws when the write fails,
    /// and then it leaves the file as it was. GamePathCheck and the lobby
    /// service both depend on that.
    /// </summary>
    /// <remarks>
    /// Two threads save: the pump saves a name that it learned, and the window
    /// saves the name of the player or the game path. The lock keeps the write
    /// and the move together, so one thread cannot move a file that another
    /// thread still writes. The name of the temporary file also holds a new
    /// GUID, so two saves never share one temporary file. The GUID covers a
    /// second ConfigStore object, and a second process, which the lock cannot
    /// reach.
    /// </remarks>
    public void Save(AppConfig config)
    {
        var folder = Path.GetDirectoryName(_path);

        lock (_gate)
        {
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            // The app writes a temporary file. Then the app moves that file
            // onto the target. File.WriteAllText truncates the target first,
            // and a crash in that moment loses every value in the file. The
            // move puts a whole file in place in one step, so a reader sees
            // the old file or the new file and never one half of each.
            var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";

            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(config, Json));
                File.Move(temporary, _path, overwrite: true);
            }
            catch
            {
                // The save failed, so the caller gets the exception. The
                // temporary file must not stay in the folder, because each
                // save now picks a new name and the files would collect.
                DeleteAndIgnoreTheResult(temporary);
                throw;
            }
        }
    }

    /// <summary>
    /// This method deletes the temporary file. It ignores a failure, because
    /// the caller already has an exception to report and that exception says
    /// more about the cause.
    /// </summary>
    private static void DeleteAndIgnoreTheResult(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static AppConfig Defaults() => new() { DisplayName = Environment.MachineName };

    /// <summary>
    /// This method copies the entries into a map with the comparer
    /// StringComparer.OrdinalIgnoreCase. It drops an entry whose node ID or
    /// name is blank. A hand-edited file can hold such an entry, and
    /// PeerDirectory would show it as a peer that never leaves.
    /// </summary>
    private static ConcurrentDictionary<string, string> CopyValidEntries(
        ConcurrentDictionary<string, string>? source)
    {
        var map = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return map;

        foreach (var (nodeId, name) in source)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(name)) continue;
            map[nodeId] = name;
        }

        return map;
    }
}
