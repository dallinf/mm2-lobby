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

    public void Save(AppConfig config)
    {
        var folder = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        // The app writes a temporary file. Then the app moves that file onto
        // the target. File.WriteAllText truncates the target first, and a crash
        // in that moment loses every value in the file.
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, Json));
        File.Move(temporary, _path, overwrite: true);
    }

    private static AppConfig Defaults() => new() { DisplayName = Environment.MachineName };

    /// <summary>
    /// This method copies the entries into a map with the comparer
    /// StringComparer.OrdinalIgnoreCase. It drops an entry whose node ID or
    /// name is blank. A hand-edited file can hold such an entry, and
    /// PeerDirectory would show it as a peer that never leaves.
    /// </summary>
    private static Dictionary<string, string> CopyValidEntries(
        Dictionary<string, string>? source)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (source is null) return map;

        foreach (var (nodeId, name) in source)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(name)) continue;
            map[nodeId] = name;
        }

        return map;
    }
}
