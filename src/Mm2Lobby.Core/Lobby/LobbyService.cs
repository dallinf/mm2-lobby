using System.Net;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Core.Lobby;

/// <summary>
/// The service owns the one refresh cycle for the whole application. StartAsync
/// runs RunCycleAsync every CyclePeriod. A test calls RunCycleAsync directly,
/// so no test waits on a timer. No other class holds a timer.
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
    private readonly TimeProvider _timeProvider;

    private ZeroTierStatus? _status;
    private ZeroTierNetwork? _network;

    // The host start time, in UTC ticks. Zero means that this app does not
    // host. A long holds the value in one word. So a reader cannot see one
    // half of the new value and one half of the old value.
    private long _hostingSince;

    private CancellationTokenSource? _loop;
    private Task? _pump;
    private readonly SemaphoreSlim _cycleGate = new(1, 1);
    private bool _namesNeedSave;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// The caller owns the configuration. The process holds one AppConfig, and
    /// the caller passes that object to this constructor. The service reads the
    /// display name from that object on every cycle. The service also saves the
    /// object. The service does not load the file, because a load here would
    /// discard a value that the caller wrote, for example the game path.
    /// </summary>
    public LobbyService(
        IZeroTierClient zeroTier,
        IPeerTransport transport,
        PeerTable table,
        PeerDirectory directory,
        ConfigStore configStore,
        AppConfig config,
        string appVersion,
        Func<DateTimeOffset> clock,
        TimeProvider? timeProvider = null)
    {
        _zeroTier = zeroTier;
        _transport = transport;
        _table = table;
        _directory = directory;
        _configStore = configStore;
        _config = config;
        _appVersion = appVersion;
        _clock = clock;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<Peer> Peers { get; private set; } = Array.Empty<Peer>();

    public bool IsHosting => Volatile.Read(ref _hostingSince) != 0;

    /// <summary>
    /// The peer that hosts a game. This property never returns this machine.
    /// Peers never holds this machine. To show that this app hosts, the window
    /// reads IsHosting and MyAddress.
    /// </summary>
    public Peer? CurrentHost => Peers.FirstOrDefault(peer => peer.Hosting);

    public string? MyAddress => _network?.Address?.ToString();

    public string DisplayName => _config.DisplayName;

    /// <summary>
    /// The exception from the last cycle that the pump ran. This property is
    /// null when that cycle completed. Mm2Lobby.Core has no logger, so this
    /// property holds the only record of a failed cycle.
    /// </summary>
    public Exception? LastCycleFault { get; private set; }

    /// <summary>
    /// The service raises this event on the thread that runs the cycle. After
    /// the first cycle, the pump thread runs each cycle. The pump thread is not
    /// the UI thread. A subscriber that changes the user interface must first
    /// move to the UI thread.
    /// </summary>
    public event Action<IReadOnlyList<Peer>>? PeersChanged;

    /// <summary>
    /// StartAsync starts the transport. It runs one cycle. Then it starts the
    /// refresh loop. Call this method once. A second call throws
    /// InvalidOperationException.
    /// </summary>
    /// <remarks>
    /// If this method throws, the service is not usable. Dispose this instance.
    /// Then construct a new one. The service cannot retry a failed start. The
    /// transport also refuses a second start.
    /// </remarks>
    public async Task StartAsync(CancellationToken ct)
    {
        if (_started)
        {
            throw new InvalidOperationException("The lobby service has already started.");
        }

        _started = true;

        _transport.DatagramReceived += OnDatagram;
        await _transport.StartAsync(Port, ct);

        // This first cycle is outside the catch of the pump on purpose. If the
        // start fails, the caller must see the exception. Then the window can
        // tell the player that the lobby did not start. Later, the pump ignores
        // a failed cycle. A stale list is better than a refresh that stops.
        await RunCycleAsync(ct);

        _loop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pump = PumpAsync(_loop.Token);
    }

    public async Task RunCycleAsync(CancellationToken ct)
    {
        await _cycleGate.WaitAsync(ct);
        try
        {
            _status = await _zeroTier.GetStatusAsync(ct);
            _network = await _zeroTier.GetNetworkAsync(_config.ZeroTierNetworkId, ct);

            await AnnounceAsync(ct);

            Peers = await _directory.GetPeersAsync(
                _status?.NodeId ?? string.Empty, _config.RememberedNames, ct);

            try
            {
                PeersChanged?.Invoke(Peers);
            }
            catch (Exception)
            {
                // A subscriber must not throw. This catch ignores an exception
                // from a subscriber, so one faulty subscriber cannot stop the
                // service. Mm2Lobby.Core has no logger. This catch discards the
                // exception.
            }

            RememberNames();
        }
        finally
        {
            _cycleGate.Release();
        }
    }

    public void StartHosting() => Volatile.Write(ref _hostingSince, _clock().UtcTicks);

    public void StopHosting() => Volatile.Write(ref _hostingSince, 0);

    private async Task AnnounceAsync(CancellationToken ct)
    {
        var status = _status;
        var network = _network;
        if (status is null || network?.Address is null) return;

        var hostingSince = Volatile.Read(ref _hostingSince);

        // PeerTable rejects a name that is too long. Every peer runs that
        // check, so a long name here stops every peer from showing this app.
        //
        // This method reads the name one time, into a local. The window owns
        // the one AppConfig, and it writes the name while the pump runs. With
        // two reads, a name of 3 characters can replace a name of 40 between
        // them, and the truncation then throws.
        var name = _config.DisplayName;
        var player = name.Length > PeerTable.MaxPlayerNameLength
            ? name[..PeerTable.MaxPlayerNameLength]
            : name;

        var announcement = new Announcement
        {
            AppVersion = _appVersion,
            NodeId = status.NodeId,
            Player = player,
            Address = network.Address.ToString(),
            Hosting = hostingSince != 0,
            StartedAt = hostingSince == 0
                ? null
                : new DateTimeOffset(hostingSince, TimeSpan.Zero),
        };

        var destination = IPv4Math.BroadcastAddress(network.Address, network.PrefixLength);
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
        // Without a status, the node ID of this machine is the empty string.
        // Then this machine is one row of Peers. The app must not write its own
        // node ID to the file. The file would keep that row, and the lobby
        // would show this machine as a peer.
        if (_status is null) return;

        foreach (var peer in Peers.Where(peer => peer.AppRunning))
        {
            if (_config.RememberedNames.TryGetValue(peer.NodeId, out var known)
                && known == peer.DisplayName)
            {
                continue;
            }

            _config.RememberedNames[peer.NodeId] = peer.DisplayName;
            _namesNeedSave = true;
        }

        if (!_namesNeedSave) return;

        try
        {
            _configStore.Save(_config);
            _namesNeedSave = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A locked or read-only file must not stop the cycle. The first
            // cycle runs inside StartAsync, and an exception there aborts the
            // start of the lobby. The flag stays set, so the next cycle writes
            // the file again.
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(CyclePeriod, _timeProvider);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
                await RunCycleAsync(ct);
                LastCycleFault = null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                LastCycleFault = e;

                // This catch ignores any exception in a cycle, for example a
                // locked configuration file or a failed send. The next tick
                // tries again. Mm2Lobby.Core has no logger, so this catch puts
                // the exception in LastCycleFault. An exception that repeats
                // leaves the peer list stale, and a player can see the stale
                // list. A stopped loop would show nothing.
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
                // This catch is a defence. The pump catches its own
                // exceptions, so the pump task does not fault.
            }
        }

        _loop?.Dispose();
        await _transport.DisposeAsync();
    }
}
