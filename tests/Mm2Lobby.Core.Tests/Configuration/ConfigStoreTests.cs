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
    public void Load_treats_a_null_game_path_and_a_null_network_id_as_empty()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(
            Path_, """{"displayName":"x","gamePath":null,"zeroTierNetworkId":null}""");

        var config = new ConfigStore(Path_).Load();

        Assert.Equal(string.Empty, config.GamePath);
        Assert.Equal(string.Empty, config.ZeroTierNetworkId);
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
        Assert.True(config.RememberedNames.ContainsKey("Ab11"));
        Assert.Contains(config.RememberedNames["Ab11"], new[] { "x", "y" });
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
    public void Save_gives_its_temporary_file_a_name_that_no_other_save_shares()
    {
        // The window and the pump both save. While both saves shared the name
        // "config.json.tmp", one save could move a file that the other save
        // still wrote, and the player lost every value in the file.
        //
        // This test puts a folder at the old shared name. A save that still
        // used that name would try to write over a folder and would throw.
        Directory.CreateDirectory(Path_ + ".tmp");

        new ConfigStore(Path_).Save(new AppConfig { DisplayName = "dallin" });

        Assert.Equal("dallin", new ConfigStore(Path_).Load().DisplayName);
    }

    [Fact]
    public void Save_leaves_no_temporary_file_behind()
    {
        var store = new ConfigStore(Path_);
        store.Save(new AppConfig { DisplayName = "dallin" });
        store.Save(new AppConfig { DisplayName = "chris" });

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public async Task Many_saves_at_the_same_time_leave_a_file_that_Load_can_read()
    {
        // Every save completes, and the file holds one whole configuration.
        // Without the lock, one thread moves a file that another thread still
        // writes, and Load answers with the defaults.
        var store = new ConfigStore(Path_);
        var names = new[] { "dallin", "chris" };

        var savers = names
            .Select(name => Task.Run(() =>
            {
                for (var count = 0; count < 60; count++)
                {
                    store.Save(new AppConfig
                    {
                        DisplayName = name,
                        ZeroTierNetworkId = "0123456789abcdef",
                    });
                }
            }))
            .ToArray();

        await Task.WhenAll(savers);

        var loaded = store.Load();
        Assert.Contains(loaded.DisplayName, names);
        Assert.Equal("0123456789abcdef", loaded.ZeroTierNetworkId);
    }

    [Fact]
    public void A_save_that_fails_throws_and_leaves_the_target_as_it_was()
    {
        // GamePathCheck restores the old game path when the save throws, so
        // Save must report a failure rather than hide it. A folder at the
        // target makes the move fail.
        Directory.CreateDirectory(Path_);

        Assert.ThrowsAny<IOException>(
            () => new ConfigStore(Path_).Save(new AppConfig { DisplayName = "dallin" }));

        Assert.True(Directory.Exists(Path_));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void A_reader_of_the_remembered_names_accepts_a_new_entry()
    {
        // JsonSerializer reads every entry of this map inside Save, and the
        // pump adds a node ID in LobbyService.RememberNames, outside the lock
        // of Save. This test holds a reader open and then adds one entry, as
        // those two threads do.
        //
        // A plain Dictionary throws InvalidOperationException on the next
        // step. Neither catch filter covers that exception, so it leaves
        // GamePathCheck.FixAsync, the rollback of that method puts back the
        // old empty game path, and the player loses the game that they chose.
        var config = new AppConfig();
        for (var count = 0; count < 100; count++)
        {
            config.RememberedNames[$"node{count:000}"] = "chris";
        }

        using var reader = config.RememberedNames.GetEnumerator();
        Assert.True(reader.MoveNext());

        config.RememberedNames["bb11223344"] = "sam";

        while (reader.MoveNext())
        {
        }
    }

    [Fact]
    public void A_save_that_fails_keeps_every_value_of_the_old_file()
    {
        // GamePathCheck puts back the old game path when Save throws, so the
        // file must still hold what it held. A folder in place of the
        // temporary file makes the next write fail.
        var store = new ConfigStore(Path_);
        store.Save(new AppConfig
        {
            DisplayName = "dallin",
            GamePath = @"C:\MM2\midtown2.exe",
            ZeroTierNetworkId = "0123456789abcdef",
            RememberedNames = { ["bb11223344"] = "chris" },
        });

        Directory.CreateDirectory(Path.Combine(_folder, "wall"));
        var blocked = new ConfigStore(Path.Combine(_folder, "wall"));

        Assert.ThrowsAny<IOException>(() => blocked.Save(new AppConfig { DisplayName = "lost" }));

        var kept = store.Load();
        Assert.Equal("dallin", kept.DisplayName);
        Assert.Equal(@"C:\MM2\midtown2.exe", kept.GamePath);
        Assert.Equal("0123456789abcdef", kept.ZeroTierNetworkId);
        Assert.Equal("chris", kept.RememberedNames["bb11223344"]);
    }

    [Fact]
    public void DefaultPath_sits_under_the_application_data_folder()
    {
        Assert.Contains("Mm2Lobby", ConfigStore.DefaultPath);
        Assert.EndsWith("config.json", ConfigStore.DefaultPath);
    }
}
