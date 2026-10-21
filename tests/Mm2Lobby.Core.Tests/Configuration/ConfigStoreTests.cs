using Mm2Lobby.Core.Configuration;

namespace Mm2Lobby.Core.Tests.Configuration;

public class ConfigStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"mm2lobby-{Guid.NewGuid():N}");

    private string Path_ => Path.Combine(_folder, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Save_then_Load_round_trips_every_field()
    {
        var store = new ConfigStore(Path_);
        var config = new AppConfig
        {
            DisplayName = "dallin",
            GamePath = @"C:\Games\midtown2.exe",
            ZeroTierNetworkId = "0123456789abcdef",
            RememberedNames = { ["bb11223344"] = "chris" },
        };

        store.Save(config);
        var loaded = store.Load();

        Assert.Equal("dallin", loaded.DisplayName);
        Assert.Equal(@"C:\Games\midtown2.exe", loaded.GamePath);
        Assert.Equal("0123456789abcdef", loaded.ZeroTierNetworkId);
        Assert.Equal("chris", loaded.RememberedNames["bb11223344"]);
    }

    [Fact]
    public void Save_creates_the_folder()
    {
        new ConfigStore(Path_).Save(new AppConfig { DisplayName = "dallin" });

        Assert.True(File.Exists(Path_));
    }

    [Fact]
    public void Load_returns_the_defaults_when_the_file_is_absent()
    {
        var config = new ConfigStore(Path_).Load();

        Assert.Equal(Environment.MachineName, config.DisplayName);
        Assert.Equal(string.Empty, config.GamePath);
        Assert.Empty(config.RememberedNames);
    }

    [Fact]
    public void Load_returns_the_defaults_when_the_file_is_malformed()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, "{ this is not json");

        var config = new ConfigStore(Path_).Load();

        Assert.Equal(Environment.MachineName, config.DisplayName);
    }

    [Fact]
    public void Load_fills_an_empty_display_name_with_the_machine_name()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, """{"displayName":"","gamePath":""}""");

        Assert.Equal(Environment.MachineName, new ConfigStore(Path_).Load().DisplayName);
    }

    [Fact]
    public void RememberedNames_ignores_the_case_of_a_node_id()
    {
        var store = new ConfigStore(Path_);
        store.Save(new AppConfig { RememberedNames = { ["BB11223344"] = "chris" } });

        Assert.Equal("chris", store.Load().RememberedNames["bb11223344"]);
    }

    [Fact]
    public void Load_treats_a_null_remembered_names_as_empty()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, """{"displayName":"x","rememberedNames":null}""");

        var config = new ConfigStore(Path_).Load();

        Assert.Equal("x", config.DisplayName);
        Assert.Empty(config.RememberedNames);
    }

    [Fact]
    public void Load_keeps_one_entry_when_node_ids_differ_only_in_case()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, """{"rememberedNames":{"AB11":"x","ab11":"y"}}""");

        var config = new ConfigStore(Path_).Load();

        Assert.Single(config.RememberedNames);
        Assert.Equal("y", config.RememberedNames["ab11"]);
    }

    [Fact]
    public void Load_skips_a_remembered_name_with_a_null_value()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, """{"displayName":"x","rememberedNames":{"bb11223344":null}}""");

        var config = new ConfigStore(Path_).Load();

        Assert.Equal("x", config.DisplayName);
        Assert.Empty(config.RememberedNames);
    }

    [Fact]
    public void Load_skips_a_remembered_name_with_a_blank_node_id()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path_, """{"rememberedNames":{"":"x","bb11223344":"chris"}}""");

        var config = new ConfigStore(Path_).Load();

        Assert.Single(config.RememberedNames);
        Assert.Equal("chris", config.RememberedNames["bb11223344"]);
    }

    [Fact]
    public void DefaultPath_sits_under_the_application_data_folder()
    {
        Assert.Contains("Mm2Lobby", ConfigStore.DefaultPath);
        Assert.EndsWith("config.json", ConfigStore.DefaultPath);
    }
}
