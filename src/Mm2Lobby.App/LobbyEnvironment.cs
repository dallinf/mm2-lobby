using System.Reflection;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Launch;
using Mm2Lobby.Core.Lobby;
using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.Processes;
using Mm2Lobby.Core.ZeroTier;
using Mm2Lobby.Windows;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.App;

/// <summary>
/// This class builds every component of the app. It loads the configuration
/// one time, in the constructor. It gives that one object to every check and
/// to every lobby service. A second load would discard a value that another
/// component wrote.
/// </summary>
/// <remarks>
/// These three steps destroy the game path when two copies exist. First the
/// player chooses the game with the preflight fix. Then a friend sends a name
/// two seconds later. Then the lobby saves its own stale copy.
/// </remarks>
public sealed class LobbyEnvironment : ILobbyEnvironment
{
    private const string FirewallRepairHint =
        "A rule can still name an old game folder, and the check cannot see that. "
        + "If you moved Midtown Madness 2, or installed it again, choose Repair. "
        + "If every check passes and MM2 still finds no host, open Windows "
        + "Defender Firewall and look at the rules for midtown2.exe. "
        + "A Block rule wins over the allow rules of this app. "
        + "An MM2 Lobby rule that you turned off also blocks the game. "
        + "The check reads the name of a rule only. It does not see a Block "
        + "rule, and it does not see a rule that you turned off.";

    private readonly ConfigStore _configStore;
    private readonly IZeroTierClient _zeroTier;
    private readonly ZeroTierFaultReader _zeroTierFaults;
    private readonly IProcessRunner _plainRunner;
    private readonly IProcessRunner _elevatedRunner;
    private readonly Func<IPeerTransport> _createTransport;
    private readonly GameLauncher _launcher;
    private readonly Func<string?> _findTheGame;
    private readonly Func<string?> _askForTheGame;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeProvider _timeProvider;

    /// <param name="configStore">The store for the one configuration file.</param>
    /// <param name="zeroTier">The client that reads ZeroTier.</param>
    /// <param name="zeroTierCommand">
    /// The ZeroTier command that the locator found. This is a value, not a
    /// function. So the app reads the file system one time, at its start, and
    /// not on every cycle of the lobby.
    /// </param>
    /// <param name="plainRunner">Runs a program with the rights of the player.</param>
    /// <param name="elevatedRunner">Runs a program with administrator rights.</param>
    /// <param name="createTransport">
    /// Builds a transport. Each lobby service needs its own transport, because
    /// a transport accepts one start.
    /// </param>
    /// <param name="processLauncher">Starts MM2.</param>
    /// <param name="findTheGame">Reads the game path from the registry.</param>
    /// <param name="askForTheGame">Opens the file picker for the game.</param>
    /// <param name="appVersion">The version of this app.</param>
    /// <param name="clock">Reads the time.</param>
    /// <param name="timeProvider">Drives the refresh timer of the lobby service.</param>
    public LobbyEnvironment(
        ConfigStore configStore,
        IZeroTierClient zeroTier,
        ZeroTierCommand zeroTierCommand,
        IProcessRunner plainRunner,
        IProcessRunner elevatedRunner,
        Func<IPeerTransport> createTransport,
        IProcessLauncher processLauncher,
        Func<string?> findTheGame,
        Func<string?> askForTheGame,
        string appVersion,
        Func<DateTimeOffset> clock,
        TimeProvider? timeProvider = null)
    {
        _configStore = configStore;
        _zeroTier = zeroTier;
        _zeroTierFaults = new ZeroTierFaultReader(plainRunner, zeroTierCommand);
        _plainRunner = plainRunner;
        _elevatedRunner = elevatedRunner;
        _createTransport = createTransport;
        _launcher = new GameLauncher(processLauncher);
        _findTheGame = findTheGame;
        _askForTheGame = askForTheGame;
        _clock = clock;
        _timeProvider = timeProvider ?? TimeProvider.System;

        AppVersion = appVersion;

        // The one load of the process.
        Config = configStore.Load();
    }

    public AppConfig Config { get; }

    public string AppVersion { get; }

    public IReadOnlyList<CheckEntry> CreateChecks() => new[]
    {
        new CheckEntry(new ZeroTierCheck(_zeroTier, Config, _zeroTierFaults)),
        new CheckEntry(new DirectPlayCheck(_elevatedRunner)),
        new CheckEntry(new GamePathCheck(Config, _configStore, _findTheGame, _askForTheGame)),
        new CheckEntry(
            new FirewallCheck(_plainRunner, _elevatedRunner, Config),
            FirewallRepairHint),
    };

    public LobbyService CreateLobby()
    {
        var table = new PeerTable(LobbyService.PeerExpiry, _clock);

        return new LobbyService(
            _zeroTier,
            _createTransport(),
            table,
            new PeerDirectory(_zeroTier, table),
            _configStore,
            Config,
            AppVersion,
            _clock,
            _timeProvider);
    }

    public void SaveConfig() => _configStore.Save(Config);

    public void LaunchGame(string gamePath) => _launcher.Launch(gamePath);

    /// <summary>Builds the environment that the real app uses.</summary>
    /// <param name="askForTheGame">Opens the file picker for the game.</param>
    public static LobbyEnvironment CreateReal(Func<string?> askForTheGame)
    {
        var plainRunner = new SystemProcessRunner();

        // The app looks for the ZeroTier command one time, here. The Windows
        // installer of ZeroTier does not add its folder to the search list of
        // Windows, so the plain name zerotier-cli does not run on Windows.
        var zeroTierCommand = ZeroTierCliLocator.Find();

        return new LobbyEnvironment(
            new ConfigStore(ConfigStore.DefaultPath),
            new ZeroTierClient(plainRunner, zeroTierCommand.FileName),
            zeroTierCommand,
            plainRunner,
            new SystemProcessRunner(elevate: true),
            () => new UdpPeerTransport(),
            new SystemProcessLauncher(),
            GameLocator.FindInRegistry,
            askForTheGame,
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            () => DateTimeOffset.UtcNow);
    }
}
