using System.Net;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Lobby;

/// <summary>
/// Owns the one refresh cycle for the whole application. StartAsync runs
/// RunCycleAsync every CyclePeriod. Tests call RunCycleAsync directly, so no
/// test waits on a timer, and no other component holds one.
/// </summary>
public sealed class LobbyService : IAsyncDisposable
{
    public const int Port = 48200;

    public static readonly TimeSpan CyclePeriod = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan PeerExpiry = TimeSpan.FromSeconds(6);

    private readonly IZeroTierClient _zeroTier;
    private readonly IPeerTransport _transport;
    private readonly PeerTable _table;
    private readonly PeerDirectory _directory;
    private readonly ConfigStore _configStore;
    private readonly AppConfig _config;
    private readonly string _appVersion;
    private readonly Func<DateTimeOffset> _clock;

    private ZeroTierStatus? _status;
    private ZeroTierNetwork? _network;
    private DateTimeOffset? _hostingSince;
    private CancellationTokenSource? _loop;
    private Task? _pump;
    private bool _started;
    private bool _disposed;

    public LobbyService(
        IZeroTierClient zeroTier,
        IPeerTransport transport,
        PeerTable table,
        PeerDirectory directory,
        ConfigStore configStore,
        string appVersion,
        Func<DateTimeOffset> clock)
    {
        _zeroTier = zeroTier;
        _transport = transport;
        _table = table;
        _directory = directory;
        _configStore = configStore;
        _appVersion = appVersion;
        _clock = clock;
        _config = configStore.Load();
    }

    public IReadOnlyList<Peer> Peers { get; private set; } = Array.Empty<Peer>();

    public bool IsHosting => _hostingSince is not null;

    public Peer? CurrentHost => Peers.FirstOrDefault(peer => peer.Hosting);

    public string? MyAddress => _network?.Address?.ToString();

    public string DisplayName => _config.DisplayName;

    public event Action<IReadOnlyList<Peer>>? PeersChanged;

    /// <summary>
    /// Starts the transport, runs one cycle, and starts the refresh loop. Call
    /// it once. A second call throws InvalidOperationException. If this method
    /// throws, the service is not usable: dispose this instance and construct
    /// a new one. The service cannot retry a failed start, because the
    /// transport also refuses a second start.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        if (_started)
        {
            throw new InvalidOperationException("The lobby service has already started.");
        }

        _started = true;

        _transport.DatagramReceived += OnDatagram;
        await _transport.StartAsync(Port, ct);

        // This first cycle is outside the pump's catch on purpose. A failed
        // start must be visible, so that the window can tell the player the
        // lobby could not start. A failed cycle later is ignored, because a
        // list that is stale is better than a refresh that has stopped.
        await RunCycleAsync(ct);

        _loop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pump = PumpAsync(_loop.Token);
    }

    public async Task RunCycleAsync(CancellationToken ct)
    {
        _status = await _zeroTier.GetStatusAsync(ct);
        _network = await _zeroTier.GetNetworkAsync(_config.ZeroTierNetworkId, ct);

        await AnnounceAsync(ct);

        Peers = await _directory.GetPeersAsync(
            _status?.NodeId ?? string.Empty, _config.RememberedNames, ct);

        RememberNames();
        PeersChanged?.Invoke(Peers);
    }

    public void StartHosting() => _hostingSince = _clock();

    public void StopHosting() => _hostingSince = null;

    private async Task AnnounceAsync(CancellationToken ct)
    {
        if (_status is null || _network?.Address is null) return;

        var hostingSince = _hostingSince;

        var announcement = new Announcement
        {
            AppVersion = _appVersion,
            NodeId = _status.NodeId,
            Player = _config.DisplayName,
            Address = _network.Address.ToString(),
            Hosting = hostingSince is not null,
            StartedAt = hostingSince,
        };

        var destination = IPv4Math.BroadcastAddress(_network.Address, _network.PrefixLength);
        await _transport.SendAsync(announcement.ToBytes(), destination, Port, ct);
    }

    private void OnDatagram(byte[] payload, IPAddress source)
    {
        var network = _network;
        if (network?.Address is null) return;

        var allowed = IPv4Math.Subnet(network.Address, network.PrefixLength);
        _table.Accept(payload, source, allowed);
    }

    private void RememberNames()
    {
        var changed = false;

        foreach (var peer in Peers.Where(peer => peer.AppRunning))
        {
            if (_config.RememberedNames.TryGetValue(peer.NodeId, out var known)
                && known == peer.DisplayName)
            {
                continue;
            }

            _config.RememberedNames[peer.NodeId] = peer.DisplayName;
            changed = true;
        }

        if (changed) _configStore.Save(_config);
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(CyclePeriod);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
                await RunCycleAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // A failed cycle must not end the loop. Any error in a cycle,
                // such as a locked config file or a failed send, is ignored,
                // and the next tick tries again. Mm2Lobby.Core has no
                // logging, so the error is lost. A fault that persists
                // leaves the peer list stale, which a player can see, where a
                // stopped loop would show nothing.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _loop?.Cancel();
        _transport.DatagramReceived -= OnDatagram;

        if (_pump is not null)
        {
            try
            {
                await _pump;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _loop?.Dispose();
        await _transport.DisposeAsync();
    }
}
