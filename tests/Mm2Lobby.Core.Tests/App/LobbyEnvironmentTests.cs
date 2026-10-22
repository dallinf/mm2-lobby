using System.Net;
using Microsoft.Extensions.Time.Testing;
using Mm2Lobby.App;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Launch;
using Mm2Lobby.Core.Lobby;
using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Processes;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.App;

/// <summary>
/// These tests cover the composition root. The one AppConfig instance is won
/// or lost here, so every test below proves that one component sees what
/// another component wrote.
/// </summary>
public class LobbyEnvironmentTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"mm2env-{Guid.NewGuid():N}");

    /// <summary>
    /// A folder that no computer holds. Only the value that a test gives the
    /// environment can carry this name into the text of a check.
    /// </summary>
    private const string TestFolder = @"X:\a folder that only this test names";

    private readonly FakeZeroTierClient _zeroTier = new();
    private readonly ZeroTierCommand _zeroTierCommand =
        new("zerotier-cli", new[] { TestFolder });

    private readonly FakeProcessRunner _plainRunner = new();
    private readonly FakeProcessRunner _elevatedRunner = new();
    private readonly List<FakePeerTransport> _transports = new();
    private readonly RecordingLauncher _launcher = new();

    private string? _chosenGamePath;
    private string? _registryGamePath;
    private string? _accountId = "S-1-5-21-9999999999-8888888888-7777777777-1001";

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed class RecordingLauncher : IProcessLauncher
    {
        public List<GameLaunchPlan> Plans { get; } = new();

        public void Start(GameLaunchPlan plan) => Plans.Add(plan);
    }

    private ConfigStore Store => new(Path.Combine(_folder, "config.json"));

    /// <summary>
    /// This method writes a real file called midtown2.exe. GameLocator reads
    /// the file system, and it names no seam for a test. The file needs no
    /// Windows, no network, and no process.
    /// </summary>
    private string WriteTheGame()
    {
        var folder = Path.Combine(_folder, "MM2");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, GameLocator.Executable);
        File.WriteAllText(path, "not a real game");
        return path;
    }

    /// <summary>
    /// This method writes the file that the environment then loads. A test
    /// that writes its own file first keeps that file.
    /// </summary>
    private void SeedTheFile()
    {
        if (File.Exists(Path.Combine(_folder, "config.json"))) return;

        Store.Save(new AppConfig
        {
            DisplayName = "me",
            ZeroTierNetworkId = "0123456789abcdef",
        });
    }

    private LobbyEnvironment Build(TimeProvider? timeProvider = null)
    {
        SeedTheFile();
        return BuildFromTheFile(timeProvider);
    }

    private LobbyEnvironment BuildFromTheFile(TimeProvider? timeProvider) => new(
        Store,
        _zeroTier,
        _zeroTierCommand,
        () => _accountId,
        _plainRunner,
        _elevatedRunner,
        () =>
        {
            var transport = new FakePeerTransport();
            _transports.Add(transport);
            return transport;
        },
        _launcher,
        () => _registryGamePath,
        () => _chosenGamePath,
        "1.0.0.0",
        () => new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero),
        timeProvider ?? new FakeTimeProvider());

    private static ICheck CheckNamed(LobbyEnvironment environment, string name)
        => environment.CreateChecks().Single(entry => entry.Check.Name == name).Check;

    [Fact]
    public void The_environment_reads_the_file_one_time()
    {
        Store.Save(new AppConfig { DisplayName = "dallin", ZeroTierNetworkId = "abc" });

        var environment = Build();

        Assert.Equal("dallin", environment.Config.DisplayName);
        Assert.Same(environment.Config, environment.Config);
    }

    [Fact]
    public async Task Every_check_reads_the_one_configuration()
    {
        var environment = Build();

        // The window writes the network ID. The check must read that value,
        // not a copy that it loaded itself.
        environment.Config.ZeroTierNetworkId = "ffffffffffffffff";

        var result = await CheckNamed(environment, "ZeroTier").RunAsync(default);

        Assert.Equal(CheckStatus.Fail, result.Status);
        Assert.Contains("ffffffffffffffff", result.Message);
    }

    [Fact]
    public async Task A_check_that_finds_the_game_writes_to_the_one_configuration()
    {
        var environment = Build();
        _chosenGamePath = WriteTheGame();

        await CheckNamed(environment, "Midtown Madness 2").FixAsync(default);

        Assert.Equal(_chosenGamePath, environment.Config.GamePath);
    }

    [Fact]
    public async Task A_check_reads_the_game_path_from_the_registry()
    {
        var environment = Build();
        _registryGamePath = WriteTheGame();

        var result = await CheckNamed(environment, "Midtown Madness 2").RunAsync(default);

        Assert.Equal(CheckStatus.Pass, result.Status);
        Assert.Equal(_registryGamePath, environment.Config.GamePath);
    }

    [Fact]
    public async Task The_lobby_service_reads_the_one_configuration()
    {
        var environment = Build();
        environment.Config.DisplayName = "dallin";

        await using var lobby = environment.CreateLobby();
        await lobby.StartAsync(default);

        var sent = Assert.Single(_transports[0].Sent);
        var announcement = Announcement.TryParse(sent.Payload);

        Assert.NotNull(announcement);
        Assert.Equal("dallin", announcement.Player);
    }

    [Fact]
    public async Task A_lobby_cycle_keeps_the_game_path_that_a_fix_wrote()
    {
        // This test is the defect of section 5.8 of the spec, in order. With
        // two copies of the configuration, step three loses the game path.
        var environment = Build();
        var game = WriteTheGame();
        _chosenGamePath = game;

        // Step one: the window builds the lobby service.
        await using var lobby = environment.CreateLobby();
        await lobby.StartAsync(default);

        // Step two: the player chooses the game with the preflight fix.
        await CheckNamed(environment, "Midtown Madness 2").FixAsync(default);

        // Step three: a friend sends a name, and the lobby saves.
        _transports[0].Deliver(
            new Announcement
            {
                AppVersion = "1.0.0.0",
                NodeId = "bb11223344",
                Player = "chris",
                Address = "10.147.17.50",
            }.ToBytes(),
            IPAddress.Parse("10.147.17.50"));

        await lobby.RunCycleAsync(default);

        var saved = Store.Load();
        Assert.Equal(game, saved.GamePath);
        Assert.Equal("chris", saved.RememberedNames["bb11223344"]);
    }

    [Fact]
    public void The_firewall_check_offers_a_repair_while_it_passes()
    {
        // The check cannot see a rule that names an old game path. So the
        // window must offer the repair beside a check that passes.
        var entry = Build().CreateChecks()
            .Single(item => item.Check.Name == "Windows Firewall");

        Assert.NotNull(entry.RepairHint);
        Assert.Contains("Repair", entry.RepairHint);
    }

    [Fact]
    public void The_firewall_hint_names_both_rules_that_the_check_cannot_see()
    {
        // Every row reads OK and MM2 still finds no host: that is the one
        // state the checks cannot see, and the state that spec section 5.6
        // calls the worst. Without these sentences the player has nothing to
        // read and nothing to report.
        //
        // The check matches on the name of a rule only. So a Block rule for
        // the game defeats it, and an MM2 Lobby rule that the player turned
        // off defeats it too. The Repair does not turn such a rule on again,
        // because the add command does not send enable=yes.
        var entry = Build().CreateChecks()
            .Single(item => item.Check.Name == "Windows Firewall");

        Assert.NotNull(entry.RepairHint);
        Assert.Contains("midtown2.exe", entry.RepairHint);
        Assert.Contains("Block rule", entry.RepairHint);
        Assert.Contains("turned off", entry.RepairHint);
    }

    [Fact]
    public void No_other_check_offers_a_repair_while_it_passes()
    {
        var others = Build().CreateChecks()
            .Where(item => item.Check.Name != "Windows Firewall");

        Assert.All(others, item => Assert.Null(item.RepairHint));
    }

    [Fact]
    public async Task The_environment_takes_the_located_ZeroTier_command_as_a_value()
    {
        // The app must read the file system one time, at its start. So the
        // environment takes the command as a value, and it cannot look for the
        // command again. Every check that it builds names the one value.
        var environment = Build();
        _zeroTier.Status = null;

        // The plain runner knows no command, so it answers NotRunExitCode.
        // The check then reports that it found no ZeroTier command.
        var first = await CheckNamed(environment, "ZeroTier").RunAsync(default);
        var second = await CheckNamed(environment, "ZeroTier").RunAsync(default);

        Assert.Contains(TestFolder, first.FixDescription);
        Assert.Contains(TestFolder, second.FixDescription);
    }

    [Fact]
    public async Task The_token_grant_runs_with_administrator_rights()
    {
        // A command with the rights of the player cannot change the
        // permission of the token file. So the grant must take the elevated
        // runner, and not the plain one.
        var environment = Build();
        _zeroTier.Status = null;
        _plainRunner.Returns(
            "zerotier-cli",
            "-j info",
            new ProcessResult(
                ZeroTierFaultReader.TokenExitCode,
                "authtoken.secret not found or readable (try again as root)",
                ""));

        await CheckNamed(environment, "ZeroTier").FixAsync(default);

        var call = Assert.Single(_elevatedRunner.Calls);
        Assert.StartsWith("icacls.exe ", call);
        Assert.Contains("authtoken.secret", call);
        Assert.Contains($"/grant *{_accountId}:(R)", call);
    }

    [Fact]
    public void The_environment_builds_the_four_checks()
    {
        var names = Build().CreateChecks().Select(item => item.Check.Name).ToList();

        Assert.Equal(
            new[] { "ZeroTier", "DirectPlay", "Midtown Madness 2", "Windows Firewall" },
            names);
    }

    [Fact]
    public async Task Each_lobby_service_gets_its_own_transport()
    {
        // A transport accepts one start, so a second service needs a second
        // transport. Without this, the Check again button cannot work.
        var environment = Build();

        await using var first = environment.CreateLobby();
        await first.StartAsync(default);
        await first.DisposeAsync();

        await using var second = environment.CreateLobby();
        await second.StartAsync(default);

        Assert.Equal(2, _transports.Count);
        Assert.NotSame(_transports[0], _transports[1]);
        Assert.Equal(LobbyService.Port, _transports[1].StartedPort);
    }

    [Fact]
    public void LaunchGame_starts_the_game_at_the_path()
    {
        Build().LaunchGame(Path.Combine(_folder, "MM2", GameLocator.Executable));

        var plan = Assert.Single(_launcher.Plans);
        Assert.EndsWith(GameLocator.Executable, plan.ExecutablePath);
    }

    [Fact]
    public void SaveConfig_writes_the_one_configuration()
    {
        var environment = Build();
        environment.Config.DisplayName = "dallin";

        environment.SaveConfig();

        Assert.Equal("dallin", Store.Load().DisplayName);
    }
}
