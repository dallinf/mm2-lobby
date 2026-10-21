using System.Text.Json;

namespace Mm2Lobby.Core.Configuration;

/// <summary>
/// Reads and writes the one configuration file. A missing or malformed file
/// loads the defaults, because a broken file must not stop a race.
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

            config.RememberedNames = Rewrap(config.RememberedNames);

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

        File.WriteAllText(_path, JsonSerializer.Serialize(config, Json));
    }

    private static AppConfig Defaults() => new() { DisplayName = Environment.MachineName };

    private static Dictionary<string, string> Rewrap(Dictionary<string, string>? source)
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
