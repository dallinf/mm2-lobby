using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Mm2Lobby.Core.Launch;
using Mm2Lobby.Core.Lobby;
using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.Preflight;

namespace Mm2Lobby.App;

/// <summary>
/// All of the logic of the window. This class holds no Avalonia type, so a
/// test drives it like any other class. MainWindow holds the layout and the
/// bindings, and it forwards each click to this class.
/// </summary>
public sealed class LobbyViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const string HostLabel = "Host a race";
    private const string StopLabel = "Stop the race";
    private const string NoHostTitle = "No one hosts a race now.";
    private const string WorkingMessage = "The app is busy. Please wait.";

    private static readonly string[] NoSteps = Array.Empty<string>();

    private readonly ILobbyEnvironment _environment;
    private readonly IWindowHost _host;
    private readonly IReadOnlyList<CheckEntry> _checks;
    private readonly CancellationTokenSource _shutdown = new();

    private LobbyService? _lobby;
    private IDisposable? _poll;
    private bool _preflightPassed;
    private bool _disposed;

    private string _displayName = string.Empty;
    private string _hostTitle = NoHostTitle;
    private string _hostAddress = string.Empty;
    private string _hostButtonText = HostLabel;
    private string _statusMessage = string.Empty;
    private string _fixMessage = string.Empty;
    private string _faultMessage = string.Empty;
    private bool _setupExpanded = true;
    private bool _canHost;
    private bool _canJoin;
    private bool _busy;

    public LobbyViewModel(ILobbyEnvironment environment, IWindowHost host)
    {
        _environment = environment;
        _host = host;
        _checks = environment.CreateChecks();

        // A hand-edited file can hold a name that is too long. PeerTable
        // rejects such a name, so the app must shorten it before the first
        // announcement.
        SetTheName(environment.Config.DisplayName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The setup list. It holds one row for every check.</summary>
    public ObservableCollection<CheckRow> CheckRows { get; } = new();

    /// <summary>The lobby list. It holds one row for every peer.</summary>
    public ObservableCollection<PeerRow> PeerRows { get; } = new();

    /// <summary>The steps that the player follows inside MM2.</summary>
    public ObservableCollection<string> JoinSteps { get; } = new();

    /// <summary>
    /// The name that the app announces. The setter writes the name to the one
    /// configuration object, and the next cycle announces it.
    /// </summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (!SetTheName(value)) return;

            SaveTheConfiguration();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The limit for the text box. PeerTable rejects a longer name, and then
    /// every friend stops showing this player. The player cannot see that,
    /// because the app excludes itself from its own list.
    /// </summary>
    public int DisplayNameMaxLength => PeerTable.MaxPlayerNameLength;

    /// <summary>
    /// True while the setup list is open. The setter is public because
    /// Expander.IsExpanded binds both ways. A private setter would refuse the
    /// write of the player who closes the panel, and the panel could then
    /// snap shut and hide the Repair button of the firewall row.
    /// </summary>
    public bool SetupExpanded
    {
        get => _setupExpanded;
        set => Set(ref _setupExpanded, value);
    }

    public string HostTitle
    {
        get => _hostTitle;
        private set => Set(ref _hostTitle, value);
    }

    /// <summary>The address that the player types into MM2. It is empty when no peer hosts.</summary>
    public string HostAddress
    {
        get => _hostAddress;
        private set
        {
            if (Set(ref _hostAddress, value)) OnPropertyChanged(nameof(HasHostAddress));
        }
    }

    public bool HasHostAddress => _hostAddress.Length > 0;

    public string HostButtonText
    {
        get => _hostButtonText;
        private set => Set(ref _hostButtonText, value);
    }

    public bool CanHost
    {
        get => _canHost;
        private set => Set(ref _canHost, value);
    }

    public bool CanJoin
    {
        get => _canJoin;
        private set => Set(ref _canJoin, value);
    }

    /// <summary>
    /// True while a check or a fix runs. Every button that starts work reads
    /// CanAct, so the window disables each of them while this property is
    /// true.
    /// </summary>
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;

            OnPropertyChanged(nameof(CanAct));
            UpdateTheButtons();
        }
    }

    /// <summary>
    /// True while the window accepts Check again, Fix and Repair. Each of
    /// those buttons starts the same work, so one property covers them.
    /// </summary>
    public bool CanAct => !_busy;

    /// <summary>The last message about a start, a fix, or the game.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (Set(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => _statusMessage.Length > 0;

    /// <summary>
    /// What the last fix reported. The checks run again after a fix, and the
    /// rows then hold the text of RunAsync, so without this property the
    /// message of FixAsync reaches nobody. The player reads here that netsh
    /// refused the rule, or that Windows needs a restart.
    /// </summary>
    public string FixMessage
    {
        get => _fixMessage;
        private set
        {
            if (Set(ref _fixMessage, value)) OnPropertyChanged(nameof(HasFixMessage));
        }
    }

    public bool HasFixMessage => _fixMessage.Length > 0;

    /// <summary>
    /// The exception from the last failed cycle. Mm2Lobby.Core has no logger,
    /// so this text is the only record that reaches the player.
    /// </summary>
    public string FaultMessage
    {
        get => _faultMessage;
        private set
        {
            if (Set(ref _faultMessage, value)) OnPropertyChanged(nameof(HasFaultMessage));
        }
    }

    public bool HasFaultMessage => _faultMessage.Length > 0;

    /// <summary>Runs the checks and starts the lobby. The window calls this method once.</summary>
    public Task StartAsync() => StartFromAButtonAsync();

    /// <summary>
    /// Runs the checks again and starts a new lobby service. This method
    /// disposes the old service first, because LobbyService.StartAsync accepts
    /// one call only.
    /// </summary>
    public Task CheckAgainAsync() => StartFromAButtonAsync();

    /// <summary>
    /// Starts the lobby for the player. FixAsync does not come here, because
    /// FixAsync writes FixMessage and the player must still read it after the
    /// checks run again.
    /// </summary>
    private Task StartFromAButtonAsync()
    {
        FixMessage = string.Empty;
        return StartTheLobbyAsync();
    }

    /// <summary>
    /// Runs the fix of one check, then runs every check again and starts a new
    /// lobby service.
    /// </summary>
    /// <param name="checkName">The Name of the check, from the row.</param>
    public async Task FixAsync(string checkName)
    {
        if (_busy || _disposed) return;

        var entry = _checks.FirstOrDefault(item => item.Check.Name == checkName);
        if (entry is null) return;

        StartTheWork();
        FixMessage = string.Empty;

        try
        {
            // The fix runs on a thread from the pool. A file picker and an
            // elevated process each block the thread that calls them, and the
            // user interface thread must stay free.
            var result = await Task.Run(
                () => entry.Check.FixAsync(_shutdown.Token), _shutdown.Token);

            // The checks run again below, and the rows then hold the text of
            // RunAsync. So the window keeps the text of the fix here. Only
            // this text names the exit code of netsh, or the restart that
            // DISM needs.
            ShowTheFixResult(result);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            EndTheWork();
            return;
        }
        catch (Exception e)
        {
            FixMessage = $"The fix for {checkName} did not finish: {e.Message}";
        }

        // Busy stays true. StartTheLobbyAsync sets it again, and the gap
        // between the two would let a second click start another run.
        await StartTheLobbyAsync();
    }

    private void ShowTheFixResult(CheckResult result)
    {
        FixMessage = string.IsNullOrWhiteSpace(result.FixDescription)
            ? $"{result.Name}: {result.Message}"
            : $"{result.Name}: {result.Message} {result.FixDescription}";
    }

    /// <summary>
    /// Starts or stops the host role. When this app takes the host role, it
    /// also starts MM2.
    /// </summary>
    public void Host()
    {
        var lobby = _lobby;
        if (lobby is null || _busy) return;

        if (lobby.IsHosting)
        {
            lobby.StopHosting();
            RefreshStatus();
            return;
        }

        lobby.StartHosting();
        RefreshStatus();
        StartTheGame();
    }

    /// <summary>
    /// Copies the address of the host, then starts MM2. The player types the
    /// address, because MM2 can refuse Ctrl+V.
    /// </summary>
    public async Task JoinAsync()
    {
        var address = _lobby?.CurrentHost?.Address?.ToString();
        if (address is null || _busy) return;

        var note = string.Empty;

        try
        {
            await _host.CopyTextAsync(address);
        }
        catch (Exception e)
        {
            // The clipboard is a convenience. The window also shows the
            // address in a large font, so the player can read it and type it.
            // The note goes in front of the message about the game, because
            // the window holds one message and the game matters more.
            note = $"The app cannot write to the clipboard ({e.Message}). ";
        }

        StartTheGame(note);
    }

    /// <summary>
    /// Reads the state of the lobby service and shows it. The window calls
    /// this method once every cycle period, because the service raises
    /// PeersChanged only after a cycle that completes. Without this read, a
    /// failed cycle would reach nobody.
    /// </summary>
    public void RefreshStatus()
    {
        var lobby = _lobby;
        var host = lobby?.CurrentHost;

        if (lobby is not null && lobby.IsHosting)
        {
            // CurrentHost never returns this machine. So the window reads
            // IsHosting and MyAddress to show the race of this player.
            //
            // MyAddress is null until a cycle reads the ZeroTier network. The
            // window must not ask the player for an address that the window
            // does not show.
            HostAddress = lobby.MyAddress ?? string.Empty;
            HostTitle = HostAddress.Length > 0
                ? "You host this race. Give the address to the others."
                : "You host this race. The app does not have your address yet. "
                    + "Wait a moment.";

            ShowTheSteps(JoinInstructions.ForHost());
        }
        else if (host?.Address is not null)
        {
            HostTitle = $"{host.DisplayName} hosts this race.";
            HostAddress = host.Address.ToString();
            ShowTheSteps(JoinInstructions.For(host.Address.ToString()));
        }
        else
        {
            HostTitle = NoHostTitle;
            HostAddress = string.Empty;
            ShowTheSteps(NoSteps);
        }

        FaultMessage = lobby?.LastCycleFault is { } fault
            ? $"The last refresh failed: {fault.Message}"
            : string.Empty;

        UpdateTheButtons();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _shutdown.Cancel();
        await StopTheLobbyAsync();
        _shutdown.Dispose();
    }

    /// <summary>
    /// Runs every check. If every check passes, this method builds a new lobby
    /// service and starts it. The checks run first, because a fix can change
    /// the configuration and the service reads the configuration when it
    /// starts.
    /// </summary>
    private async Task StartTheLobbyAsync()
    {
        if (_disposed) return;

        // The old message belongs to the attempt that ended. Without this, a
        // Check again that works still shows "The lobby did not start".
        StatusMessage = string.Empty;

        // The work starts before the first await. StopTheLobbyAsync waits for
        // the old service, and a second click in that gap would start another
        // run: two services, and a timer that no field holds any more.
        //
        // A fix calls this method, and the fix already turned the buttons
        // off. The message still has to go back, because the line above took
        // it away and the checks below run netsh and zerotier-cli.
        StartTheWork();

        try
        {
            await StopTheLobbyAsync();
            await RunTheChecksAsync();

            if (!_preflightPassed || _disposed) return;

            var lobby = _environment.CreateLobby();
            lobby.PeersChanged += OnPeersChanged;

            // The first cycle runs inside StartAsync and raises PeersChanged.
            // The field must already hold the service, because the subscriber
            // reads it.
            _lobby = lobby;

            try
            {
                await lobby.StartAsync(_shutdown.Token);
            }
            catch (Exception e)
            {
                // A failed start leaves the service unusable, and StartAsync
                // accepts one call. So the window drops this instance. The
                // Check again button builds another one.
                _lobby = null;
                lobby.PeersChanged -= OnPeersChanged;
                await lobby.DisposeAsync();

                if (!_shutdown.IsCancellationRequested)
                {
                    StatusMessage = $"The lobby did not start: {e.Message}";
                }

                return;
            }

            _poll = _host.Repeat(LobbyService.CyclePeriod, RefreshStatus);
            RefreshStatus();
        }
        finally
        {
            // The work covers the whole run, not only the checks. The
            // buttons therefore stay off while the service starts.
            EndTheWork();
            UpdateTheButtons();
        }
    }

    private async Task StopTheLobbyAsync()
    {
        _poll?.Dispose();
        _poll = null;

        var lobby = _lobby;
        _lobby = null;
        PeerRows.Clear();

        if (lobby is null) return;

        lobby.PeersChanged -= OnPeersChanged;
        await lobby.DisposeAsync();
    }

    /// <summary>
    /// Runs every check. The caller owns Busy, because the caller also starts
    /// the lobby service and the buttons must stay off for the whole run.
    /// </summary>
    private async Task RunTheChecksAsync()
    {
        try
        {
            var runner = new PreflightRunner(
                _checks.Select(entry => entry.Check).ToList());

            var results = await runner.RunAllAsync(_shutdown.Token);

            ShowTheChecks(results);
            _preflightPassed = PreflightRunner.AllPassed(results);
            SetupExpanded = !_preflightPassed;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            _preflightPassed = false;
        }
    }

    private void ShowTheChecks(IReadOnlyList<CheckResult> results)
    {
        CheckRows.Clear();

        foreach (var result in results)
        {
            var entry = _checks.FirstOrDefault(item => item.Check.Name == result.Name);
            CheckRows.Add(CheckRow.From(result, entry?.RepairHint));
        }
    }

    private void OnPeersChanged(IReadOnlyList<Peer> peers)
        => _host.Post(() => ShowThePeers(peers));

    private void ShowThePeers(IReadOnlyList<Peer> peers)
    {
        PeerRows.Clear();

        foreach (var peer in peers)
        {
            PeerRows.Add(PeerRow.From(peer, _environment.AppVersion));
        }

        RefreshStatus();
    }

    private void ShowTheSteps(IReadOnlyList<string> steps)
    {
        // The window reads the state once every cycle. Without this
        // comparison, the list would rebuild every two seconds and the text
        // would flicker.
        if (JoinSteps.SequenceEqual(steps)) return;

        JoinSteps.Clear();
        foreach (var step in steps) JoinSteps.Add(step);
    }

    private void UpdateTheButtons()
    {
        var lobby = _lobby;
        var ready = _preflightPassed && lobby is not null && !_busy;

        CanHost = ready;
        CanJoin = ready && !lobby!.IsHosting && lobby.CurrentHost?.Address is not null;
        HostButtonText = lobby?.IsHosting == true ? StopLabel : HostLabel;
    }

    /// <param name="note">Text that goes in front of the message. It can be empty.</param>
    private void StartTheGame(string note = "")
    {
        var path = _environment.Config.GamePath;

        try
        {
            _environment.LaunchGame(path);
            StatusMessage = note + "MM2 started. Follow the steps above.";
        }
        catch (Exception e)
        {
            StatusMessage = note + $"MM2 did not start: {e.Message}";
        }
    }

    /// <summary>
    /// Writes the name to the one configuration object. It returns false when
    /// the name does not change.
    /// </summary>
    private bool SetTheName(string? value)
    {
        var name = value ?? string.Empty;

        if (name.Length > PeerTable.MaxPlayerNameLength)
        {
            name = name[..PeerTable.MaxPlayerNameLength];
        }

        _environment.Config.DisplayName = name;

        if (name == _displayName) return false;

        _displayName = name;
        return true;
    }

    private void SaveTheConfiguration()
    {
        try
        {
            _environment.SaveConfig();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"The app cannot save the settings: {e.Message}";
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    /// <summary>
    /// Turns the buttons off and says why. A button that the window disables
    /// tells the player nothing about the reason, and dism takes tens of
    /// seconds.
    ///
    /// This method writes the message on every call. A fix calls this method.
    /// The start that follows the fix calls this method again. Busy is
    /// already true at that second call, so a setter alone would write
    /// nothing.
    /// </summary>
    private void StartTheWork()
    {
        Busy = true;
        StatusMessage = WorkingMessage;
    }

    private void EndTheWork()
    {
        Busy = false;

        // Another message can have taken the place of this one, for example
        // the message about MM2. Only this one belongs to the work.
        if (_statusMessage == WorkingMessage) StatusMessage = string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
