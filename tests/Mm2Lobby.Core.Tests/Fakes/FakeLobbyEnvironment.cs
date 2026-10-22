using Microsoft.Extensions.Time.Testing;
using Mm2Lobby.App;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Lobby;
using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Tests.Fakes;

/// <summary>
/// This fake stands for the system. It builds a real LobbyService over fakes,
/// so a test drives the window against the real service. The FakeTimeProvider
/// never advances by itself, so the pump of the service never ticks and no
/// test waits.
/// </summary>
public sealed class FakeLobbyEnvironment : ILobbyEnvironment, IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"mm2window-{Guid.NewGuid():N}");

    public FakeLobbyEnvironment()
    {
        ConfigStore = new ConfigStore(Path.Combine(_folder, "config.json"));
        Config = new AppConfig
        {
            DisplayName = "me",
            ZeroTierNetworkId = "0123456789abcdef",
            GamePath = @"C:\Games\MM2\midtown2.exe",
        };
    }

    public AppConfig Config { get; }

    public string AppVersion { get; set; } = "1.0.0.0";

    public ConfigStore ConfigStore { get; }

    public IZeroTierClient ZeroTier { get; set; } = new FakeZeroTierClient();

    public FakeTimeProvider Time { get; } = new();

    public DateTimeOffset Now { get; set; } = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The checks that CreateChecks returns. A test fills this list.</summary>
    public List<CheckEntry> Checks { get; } = new();

    /// <summary>Every transport that CreateLobby built, in order.</summary>
    public List<FakePeerTransport> Transports { get; } = new();

    /// <summary>
    /// Builds the transport for the next lobby service. A test replaces it to
    /// make a start fail.
    /// </summary>
    public Func<IPeerTransport>? CreateTransport { get; set; }

    public List<string> LaunchedPaths { get; } = new();

    public Exception? LaunchFault { get; set; }

    public Exception? SaveFault { get; set; }

    public int SaveCount { get; private set; }

    public int CreateLobbyCount { get; private set; }

    /// <summary>Every lobby service that CreateLobby built, in order.</summary>
    public List<LobbyService> Lobbies { get; } = new();

    public IReadOnlyList<CheckEntry> CreateChecks() => Checks;

    public LobbyService CreateLobby()
    {
        CreateLobbyCount++;

        var transport = CreateTransport?.Invoke() ?? NewTransport();
        var table = new PeerTable(LobbyService.PeerExpiry, () => Now);

        var lobby = new LobbyService(
            ZeroTier,
            transport,
            table,
            new PeerDirectory(ZeroTier, table),
            ConfigStore,
            Config,
            AppVersion,
            () => Now,
            Time);

        Lobbies.Add(lobby);
        return lobby;
    }

    public void SaveConfig()
    {
        SaveCount++;

        if (SaveFault is not null) throw SaveFault;

        ConfigStore.Save(Config);
    }

    public void LaunchGame(string gamePath)
    {
        if (LaunchFault is not null) throw LaunchFault;

        LaunchedPaths.Add(gamePath);
    }

    /// <summary>Builds a transport and remembers it.</summary>
    public FakePeerTransport NewTransport()
    {
        var transport = new FakePeerTransport();
        Transports.Add(transport);
        return transport;
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
