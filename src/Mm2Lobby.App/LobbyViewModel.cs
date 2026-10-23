using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Mm2Lobby.Core.Configuration;
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

    /// <summary>
    /// The label of the launch button for the host. The host starts the race,
    /// so the button can name the race.
    /// </summary>
    private const string StartRaceLabel = "Start the race";

    /// <summary>
    /// The label of the launch button for a player who joins. That player does
    /// not start the race, so the button names the game only. The window also
    /// shows this label while the button waits for Host or Join, because the
    /// label is true for every player who starts MM2.
    /// </summary>
    private const string StartGameLabel = "Start the game";

    /// <summary>
    /// The message for a player who joined a host that then stopped. The app
    /// takes the launch button away at the same time, and a button that
    /// disappears tells the player nothing about the reason.
    /// </summary>
    private const string HostStoppedMessage =
        "The host stopped the race. Wait for a new host.";

    /// <summary>
    /// The advice for a network ID that breaks the rule. The app has no
    /// settings screen, so this text is the only place that tells the player
    /// the rule and the page that shows the ID.
    /// </summary>
    /// <remarks>
    /// The text reads the length from NetworkIdRule. A message that holds the
    /// number itself becomes a lie as soon as the rule changes.
    /// </remarks>
    private static readonly string BadNetworkIdMessage =
        $"The app did not save that network ID. An ID has {NetworkIdRule.Length} "
        + "characters, and each character is 0-9 or a-f. ZeroTier Central at "
        + "https://my.zerotier.com shows the ID of your network.";

    /// <summary>
    /// The message after a saved ID. The checks read the ID again on every
    /// run, so Check again starts the lobby and the player does not restart
    /// the app.
    /// </summary>
    private const string SavedNetworkIdMessage =
        "The app saved the network ID. Choose Check again to start the lobby.";

    /// <summary>
    /// The message of an ordinary start. The app showed this sentence before
    /// the lobby path existed, and it shows the same sentence after a fallback.
    /// </summary>
    private const string OrdinaryStartMessage = "MM2 started. Follow the steps above.";

    private static readonly string[] NoSteps = Array.Empty<string>();

    /// <summary>
    /// What the player announced. The launch button reads this value for its
    /// label and for its gate, because the window must not offer to start MM2
    /// before the player hosts a race or joins one.
    /// </summary>
    private enum LaunchRole
    {
        /// <summary>The player pressed neither Host nor Join.</summary>
        None,

        /// <summary>The player hosts the race.</summary>
        Host,

        /// <summary>The player joins the race of another player.</summary>
        Join,
    }

    /// <summary>
    /// What the last press of the launch button did. The steps follow this
    /// value, because a step that names the wrong path is a lie: a game that
    /// the lobby put in the race asks the player for no address, and a game
    /// that the app started the ordinary way asks him for one.
    /// </summary>
    private enum LaunchPath
    {
        /// <summary>The player pressed the launch button never, or not since the role changed.</summary>
        None,

        /// <summary>The DirectPlay lobby started the game in the race.</summary>
        Lobby,

        /// <summary>The app started the game, and the player types the address.</summary>
        Ordinary,
    }

    private readonly ILobbyEnvironment _environment;
    private readonly IWindowHost _host;
    private readonly IReadOnlyList<CheckEntry> _checks;
    private readonly CancellationTokenSource _shutdown = new();

    private LobbyService? _lobby;
    private IDisposable? _poll;
    private bool _preflightPassed;
    private bool _advisoryChecksPassed;
    private bool _disposed;

    /// <summary>
    /// The live session of the last lobby launch that worked. The app holds
    /// one session, because one game runs. ILobbySession holds the DirectPlay
    /// interface and the memory that the game reads, so the window keeps this
    /// object for as long as the game may run.
    /// </summary>
    private ILobbySession? _session;

    private string _displayName = string.Empty;
    private string _networkId = string.Empty;
    private string _networkIdMessage = string.Empty;
    private string _hostTitle = NoHostTitle;
    private string _hostAddress = string.Empty;
    private string _hostButtonText = HostLabel;
    private string _startGameButtonText = StartGameLabel;
    private string _statusMessage = string.Empty;
    private string _fixMessage = string.Empty;
    private string _faultMessage = string.Empty;
    private string _launchReport = string.Empty;
    private bool _setupExpanded = true;
    private bool _canHost;
    private bool _canJoin;
    private bool _canStartGame;
    private bool _busy;
    private LaunchRole _role = LaunchRole.None;
    private LaunchPath _path = LaunchPath.None;

    public LobbyViewModel(ILobbyEnvironment environment, IWindowHost host)
    {
        _environment = environment;
        _host = host;
        _checks = environment.CreateChecks();

        // A hand-edited file can hold a name that is too long. PeerTable
        // rejects such a name, so the app must shorten it before the first
        // announcement.
        SetTheName(environment.Config.DisplayName);

        // The box shows the ID that the file holds, even a bad one. The
        // ZeroTier check then reports the fault, and the player sees the text
        // that caused it.
        _networkId = environment.Config.ZeroTierNetworkId;
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
    /// The ID of the ZeroTier network of the group. The setter writes a valid
    /// ID to the one configuration object and saves the file. The ZeroTier
    /// check and the lobby service read the ID from that object, so Check
    /// again starts the lobby with the new ID.
    /// </summary>
    /// <remarks>
    /// The window gives this box no MaxLength. A pasted ID can carry a space
    /// at each end, and a limit of 16 would cut two characters off such a
    /// paste. NetworkIdRule removes the spaces instead.
    /// </remarks>
    public string ZeroTierNetworkId
    {
        get => _networkId;
        set => SetTheNetworkId(value);
    }

    /// <summary>
    /// What the last write of the network ID did. The text names the rule
    /// after a value that breaks it, and it names the Check again button after
    /// a save.
    /// </summary>
    public string NetworkIdMessage
    {
        get => _networkIdMessage;
        private set
        {
            if (Set(ref _networkIdMessage, value)) OnPropertyChanged(nameof(HasNetworkIdMessage));
        }
    }

    public bool HasNetworkIdMessage => _networkIdMessage.Length > 0;

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
    /// True while the window accepts the launch button. The player presses
    /// Host or Join first, because the app shows the address of the race
    /// before MM2 covers the screen.
    /// </summary>
    public bool CanStartGame
    {
        get => _canStartGame;
        private set => Set(ref _canStartGame, value);
    }

    /// <summary>
    /// The label of the launch button. The word depends on the role, because
    /// one word would be untrue for one of the two players.
    /// </summary>
    public string StartGameButtonText
    {
        get => _startGameButtonText;
        private set => Set(ref _startGameButtonText, value);
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

    /// <summary>
    /// The trail of calls of the last lobby launch. The window keeps this text
    /// behind a panel that the player opens, because the step of a failed
    /// launch asks him to send a report to the person who runs the group. Only
    /// this text holds the answer of each DirectPlay call.
    /// </summary>
    public string LaunchReport
    {
        get => _launchReport;
        private set
        {
            if (Set(ref _launchReport, value)) OnPropertyChanged(nameof(HasLaunchReport));
        }
    }

    public bool HasLaunchReport => _launchReport.Length > 0;

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
    /// Starts or stops the host role. This method also copies the address of
    /// this app to the clipboard, so the host can paste that address to the
    /// others.
    /// </summary>
    /// <remarks>
    /// This method does not start MM2. MM2 covers the whole screen, and the
    /// host must read his address and give it to the others first. The launch
    /// button starts MM2.
    /// </remarks>
    public async Task HostAsync()
    {
        var lobby = _lobby;
        if (lobby is null || _busy) return;

        if (lobby.IsHosting)
        {
            lobby.StopHosting();

            // The race ended. A live launch button would start MM2 for a race
            // that nobody hosts.
            TakeTheRole(LaunchRole.None);
            RefreshStatus();
            return;
        }

        lobby.StartHosting();

        // This player is the host now. A Join that came first does not hold
        // any more, so this line also takes that role away.
        TakeTheRole(LaunchRole.Host);
        RefreshStatus();

        // MyAddress is null until a cycle reads the ZeroTier network. The app
        // copies nothing in that state, and HostTitle tells the player to
        // wait.
        StatusMessage = lobby.MyAddress is { } address
            ? await CopyTheAddressAsync(address)
            : string.Empty;
    }

    /// <summary>
    /// Copies the address of the host. The player types that address into MM2,
    /// because MM2 can refuse Ctrl+V.
    /// </summary>
    /// <remarks>
    /// This method does not start MM2, for the reason that HostAsync gives.
    /// The launch button starts MM2.
    /// </remarks>
    public async Task JoinAsync()
    {
        var address = _lobby?.CurrentHost?.Address?.ToString();
        if (address is null || _busy) return;

        TakeTheRole(LaunchRole.Join);

        // The role changed, so the steps of the last launch do not hold.
        ShowTheSteps(TheStepsForNow());
        UpdateTheButtons();

        StatusMessage = await CopyTheAddressAsync(address);
    }

    /// <summary>
    /// Starts MM2. The player presses Host or Join first, reads the address
    /// that the window shows, and gives that address to the others. Then this
    /// method covers the screen with the game.
    /// <para>
    /// The method takes the lobby path when the app can take it, and it takes
    /// the ordinary path in every other case. The window then shows the steps
    /// of the path that it took.
    /// </para>
    /// <para>
    /// WARNING: call this method on the thread that holds the window. The
    /// launcher calls CoInitializeEx with COINIT_APARTMENTTHREADED, and
    /// Program.Main marks that thread STAThread. A thread from the pool joins
    /// the other apartment, and Windows then gives the DirectPlay interface to
    /// an apartment that ends with the work item.
    /// </para>
    /// </summary>
    public void StartGame()
    {
        // The same gate as Host and Join. This method starts a process, so a
        // click that arrives after the state changed must start nothing.
        if (_role == LaunchRole.None || !_preflightPassed || _lobby is null || _busy) return;

        // The button stays on after this launch, and this method does not
        // refuse a second press. MM2 is flaky and it sometimes stops, so the
        // player must be able to start it again. A guard here would cost him
        // his place in the lobby.
        StartTheGame();
    }

    /// <summary>
    /// Puts the address on the clipboard. It returns an empty text after a
    /// copy that works, and a note after a copy that fails.
    /// </summary>
    private async Task<string> CopyTheAddressAsync(string address)
    {
        try
        {
            await _host.CopyTextAsync(address);
            return string.Empty;
        }
        catch (Exception e)
        {
            // The clipboard is a convenience. The window also shows the
            // address in a large font, so the note sends the player to it.
            return $"The app cannot write to the clipboard ({e.Message}). "
                + "Read the address above.";
        }
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
        }
        else if (host?.Address is not null)
        {
            HostTitle = $"{host.DisplayName} hosts this race.";
            HostAddress = host.Address.ToString();
        }
        else
        {
            HostTitle = NoHostTitle;
            HostAddress = string.Empty;

            // The host of this player stopped the race. That address now
            // belongs to a race that ended, so the launch button goes away.
            // A button that disappears tells the player nothing, so the
            // message gives him the reason.
            if (_role == LaunchRole.Join)
            {
                TakeTheRole(LaunchRole.None);
                StatusMessage = HostStoppedMessage;
            }
        }

        // One method answers for the steps, and the window calls it on every
        // cycle. So a launch that needed no address cannot lose its steps two
        // seconds later.
        ShowTheSteps(TheStepsForNow());

        FaultMessage = lobby?.LastCycleFault is { } fault
            ? $"The last refresh failed: {fault.Message}"
            : string.Empty;

        UpdateTheButtons();
    }

    /// <summary>
    /// Gives the steps that hold now. The list follows the state of the lobby
    /// and the path of the last launch.
    /// </summary>
    /// <remarks>
    /// The window must never tell the player to do something that the path
    /// does not need. After a lobby launch the game sits in the race already,
    /// and the step "Type the host address" would be false. After an ordinary
    /// launch that step is the one that the player needs most.
    /// </remarks>
    private IReadOnlyList<string> TheStepsForNow()
    {
        var lobby = _lobby;

        if (lobby is not null && lobby.IsHosting)
        {
            return _path == LaunchPath.Lobby
                ? JoinInstructions.ForTheHostInTheRace()
                : JoinInstructions.ForHost();
        }

        if (lobby?.CurrentHost?.Address is { } address)
        {
            return _path == LaunchPath.Lobby
                ? JoinInstructions.ForThePlayerInTheRace()
                : JoinInstructions.For(address.ToString());
        }

        return NoSteps;
    }

    /// <summary>
    /// Writes the role of the player, and forgets the path of the last launch.
    /// </summary>
    /// <remarks>
    /// The steps follow that path. A player who hosted through the lobby, then
    /// stopped the race, and then joined another host would otherwise still
    /// read that the game sits in a race. He must press the launch button
    /// again, and the window must ask nothing of him until he does.
    /// </remarks>
    private void TakeTheRole(LaunchRole role)
    {
        _role = role;
        _path = LaunchPath.None;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _shutdown.Cancel();
        await StopTheLobbyAsync();

        // The session goes last, and it goes on the thread that closed the
        // window. The same thread called Launch, so the release reaches the
        // apartment that holds the DirectPlay interface. A session whose game
        // still runs releases nothing, and the process ends in any case.
        _session?.Dispose();
        _session = null;

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

        // A new service hosts nothing, and it knows no host. So the role of
        // the player goes with the old service, and the launch button waits
        // for Host or Join again.
        TakeTheRole(LaunchRole.None);

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

            // The advisory checks buy the lobby path and nothing else, so the
            // window reads them here and PreflightRunner.AllPassed reads the
            // blocking results. A list that holds no advisory check gives
            // true, as an empty list does.
            _advisoryChecksPassed = results
                .Where(result => !result.Blocks)
                .All(result => result.Status == CheckStatus.Pass);

            // The panel opens for an advisory check too, and only the gate on
            // Host and Join lets that check go. A panel that closed over an
            // advisory row would hide the one thing that the app offers the
            // player, and no player would ever press that Fix.
            SetupExpanded = results.Any(result => result.Status != CheckStatus.Pass);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // No test can see the second line, for the reason that
            // MarkTheChecksStale gives. Both flags go together all the same.
            _preflightPassed = false;
            _advisoryChecksPassed = false;
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

        // The launch button carries the gate of Host and Join, and it also
        // waits for one of those two presses.
        CanStartGame = ready && _role != LaunchRole.None;
        StartGameButtonText = _role == LaunchRole.Host ? StartRaceLabel : StartGameLabel;
    }

    private void StartTheGame()
    {
        var result = TryTheRace();

        if (result is { Started: true, Session: { } session })
        {
            // The app holds this session for as long as the game may run. It
            // owns the DirectPlay interface and the memory that MM2 reads
            // about three seconds after the launch.
            _session = session;
            _path = LaunchPath.Lobby;
            StatusMessage = result.Message;
        }
        else
        {
            // The app does what it did before the lobby path existed. The
            // window shows the address, and the player types it into MM2. No
            // player is worse off than he was.
            _path = LaunchPath.Ordinary;
            StatusMessage = StartTheGameTheOrdinaryWay() + AdviceOf(result);
        }

        // The steps follow the path that this press took. Without this line
        // the window could ask a player to type an address that the lobby
        // already gave to the game.
        ShowTheSteps(TheStepsForNow());
    }

    /// <summary>
    /// Asks the launcher to start MM2 in the race. It returns null when the
    /// app asked nothing, and the window then says nothing about a path that
    /// it did not take.
    /// </summary>
    private LobbyLaunchResult? TryTheRace()
    {
        // The two advisory checks buy this path and nothing else. A row that
        // does not pass already names the reason and offers the Fix, so the
        // window adds no second message here.
        if (!_advisoryChecksPassed) return null;

        var launcher = _environment.LobbyLauncher;

        // The launcher answers for this computer and this process: Windows,
        // the bit width, and the DirectPlay component. A false answer means
        // that this path never opens, so the window stays quiet about it.
        if (!launcher.CanLaunch) return null;

        if (!TheSessionIsFree()) return null;

        var request = TheRequest();
        if (request is null) return null;

        try
        {
            var result = launcher.Launch(request);
            LaunchReport = result.Detail;
            return result;
        }
        catch (Exception e)
        {
            // The launcher promises to throw nothing. The window catches all
            // the same, because an exception here would take the ordinary
            // start away too, and the player would get no game at all.
            LaunchReport = $"The launcher threw {e.GetType().Name}: {e.Message}";
            return LobbyLaunchResult.Failure(LobbyLaunchFault.Unexpected, LaunchReport);
        }
    }

    /// <summary>
    /// Builds the request for the role of this player. The app reads the
    /// address of the host from the announcement, so the player types nothing.
    /// </summary>
    private LobbyLaunchRequest? TheRequest()
    {
        if (_role == LaunchRole.Host) return LobbyLaunchRequest.ToHost(_displayName);

        var address = _lobby?.CurrentHost?.Address?.ToString();

        return address is null ? null : LobbyLaunchRequest.ToJoin(address, _displayName);
    }

    /// <summary>
    /// Closes the session of an earlier launch. It returns true when the app
    /// holds no session any more.
    /// </summary>
    /// <remarks>
    /// The launch button takes no guard against a second press. MM2 stops, and
    /// the player must start it again without a new Host, because a new Host
    /// would cost him his place in the lobby. So the bound on the sessions
    /// lives here and not on the button.
    ///
    /// A session releases the DirectPlay interface only after the game process
    /// ends. The press that follows a crash therefore finds no game, the old
    /// session closes, and the app takes one new session in its place. A press
    /// while the game still runs closes nothing, and the app then starts the
    /// game the ordinary way. The app holds one session in every case.
    /// </remarks>
    private bool TheSessionIsFree()
    {
        if (_session is null) return true;

        _session.Dispose();

        if (_session.IsOpen) return false;

        _session = null;
        return true;
    }

    /// <summary>
    /// Starts MM2 the way that the app started it before the lobby path
    /// existed. It returns the message for the player.
    /// </summary>
    private string StartTheGameTheOrdinaryWay()
    {
        var path = _environment.Config.GamePath;

        try
        {
            _environment.LaunchGame(path);
            return OrdinaryStartMessage;
        }
        catch (Exception e)
        {
            return $"MM2 did not start: {e.Message}";
        }
    }

    /// <summary>
    /// Gives the text of a lobby launch that failed. It gives an empty text
    /// when the app made no attempt.
    /// </summary>
    private static string AdviceOf(LobbyLaunchResult? result)
    {
        if (result is null) return string.Empty;

        return result.PlayerAction is { } action
            ? $" {result.Message} {action}"
            : $" {result.Message}";
    }

    /// <summary>
    /// Writes the name to the one configuration object. It returns false when
    /// the name does not change.
    /// </summary>
    private bool SetTheName(string? value)
    {
        var name = value ?? string.Empty;

        // A blank name makes the player invisible. PeerTable rejects an
        // announcement that carries no name, so every friend drops the
        // datagram, and the player cannot see that, because the app excludes
        // itself from its own list. ConfigStore.Load puts the computer name in
        // the place of a blank name at the start, and this method does the same
        // at run time. So a player who clears the box, and a player who closes
        // the window with an empty box, both keep a name.
        if (string.IsNullOrWhiteSpace(name)) name = Environment.MachineName;

        if (name.Length > PeerTable.MaxPlayerNameLength)
        {
            name = name[..PeerTable.MaxPlayerNameLength];
        }

        _environment.Config.DisplayName = name;

        if (name == _displayName) return false;

        _displayName = name;
        return true;
    }

    /// <summary>
    /// Writes a network ID to the one configuration object. A value that
    /// breaks the rule of NetworkIdRule does not reach that object, and the
    /// message then gives the player the rule.
    /// </summary>
    private void SetTheNetworkId(string? value)
    {
        var typed = value ?? string.Empty;

        if (!NetworkIdRule.TryRead(typed, out var id))
        {
            // The box keeps the text of the player. A box that empties itself
            // takes away the value that the player must correct.
            ShowTheNetworkId(typed);
            NetworkIdMessage = BadNetworkIdMessage;
            return;
        }

        var isNew = id != _environment.Config.ZeroTierNetworkId;

        // The box shows the trimmed ID in lower case, which is the form that
        // the file holds.
        ShowTheNetworkId(id);

        if (!isNew)
        {
            // The binding writes on every loss of the focus. The same ID must
            // not write the file again, and the old message must go, because
            // it belongs to an earlier write.
            NetworkIdMessage = string.Empty;
            return;
        }

        _environment.Config.ZeroTierNetworkId = id;
        SaveTheConfiguration();
        NetworkIdMessage = SavedNetworkIdMessage;
        MarkTheChecksStale();
    }

    /// <summary>
    /// Takes the result off every check row, and takes Host and Join away.
    /// The checks ran against the old network ID, and the ZeroTier check reads
    /// that ID. So a row that kept its Pass would assert something that no
    /// longer holds.
    /// </summary>
    /// <remarks>
    /// The window does not offer an action whose condition it cannot verify.
    /// The checks gate the two buttons, and a live Host button promises a race
    /// that the app cannot give: a service that cannot find the new network
    /// sends no announcement, and no friend sees the race. A stale row above
    /// two live buttons tells the player the same lie with the buttons.
    ///
    /// This method also opens the setup panel. The Check again button sits
    /// inside that panel, and the panel closes itself once every check passes.
    /// A closed panel would hide the button that the message names.
    /// </remarks>
    private void MarkTheChecksStale()
    {
        _preflightPassed = false;

        // No test can see this line, because _preflightPassed gates the launch
        // button first and the line above already took that away. The line
        // stands here all the same: the rule of this method is that no result
        // of the last run holds, and a reader must not have to prove the
        // exception for one of the six checks.
        _advisoryChecksPassed = false;

        UpdateTheButtons();

        if (CheckRows.Count == 0) return;

        var names = CheckRows.Select(row => row.Name).ToList();

        CheckRows.Clear();
        foreach (var name in names) CheckRows.Add(CheckRow.Stale(name));

        SetupExpanded = true;
    }

    private void ShowTheNetworkId(string value)
    {
        if (value == _networkId) return;

        _networkId = value;
        OnPropertyChanged(nameof(ZeroTierNetworkId));
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
