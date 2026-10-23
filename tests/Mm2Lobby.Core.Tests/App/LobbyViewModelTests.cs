using System.Net;
using Mm2Lobby.App;
using Mm2Lobby.Core.Launch;
using Mm2Lobby.Core.Lobby;
using Mm2Lobby.Core.Peers;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Core.ZeroTier;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.Core.Tests.App;

public class LobbyViewModelTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private readonly FakeLobbyEnvironment _environment = new();
    private readonly FakeWindowHost _host = new();

    public void Dispose() => _environment.Dispose();

    /// <summary>
    /// This client fails the cycle that the test names. LobbyServiceTests uses
    /// the same shape, so the pump runs without a sleep and without a poll.
    /// </summary>
    private sealed class ScriptedZeroTierClient : IZeroTierClient
    {
        private readonly FakeZeroTierClient _inner = new();
        private readonly object _gate = new();
        private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = new();

        public Func<int, Exception?> FailOn { get; set; } = _ => null;

        public int StatusCalls { get; private set; }

        public Task WhenCalled(int count)
        {
            lock (_gate)
            {
                if (StatusCalls >= count) return Task.CompletedTask;

                var reached = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                _waiters.Add((count, reached));
                return reached.Task;
            }
        }

        public Task<ZeroTierStatus?> GetStatusAsync(CancellationToken ct)
        {
            int call;
            lock (_gate)
            {
                call = ++StatusCalls;
                foreach (var waiter in _waiters.Where(item => item.Count <= call))
                {
                    waiter.Reached.TrySetResult();
                }
            }

            var failure = FailOn(call);
            if (failure is not null) throw failure;

            return _inner.GetStatusAsync(ct);
        }

        public Task<ZeroTierNetwork?> GetNetworkAsync(string networkId, CancellationToken ct)
            => _inner.GetNetworkAsync(networkId, ct);

        public Task<IReadOnlyDictionary<string, LinkQuality>> GetLinkQualityAsync(
            CancellationToken ct)
            => _inner.GetLinkQualityAsync(ct);
    }

    private sealed class FailingTransport : IPeerTransport
    {
        public event Action<byte[], IPAddress>? DatagramReceived;

        public Task StartAsync(int port, CancellationToken ct)
        {
            DatagramReceived?.Invoke(Array.Empty<byte>(), IPAddress.Any);
            throw new IOException("the port is busy");
        }

        public Task SendAsync(byte[] payload, IPAddress destination, int port, CancellationToken ct)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Adds a check that passes.</summary>
    private FakeCheck AddCheck(string name, CheckStatus status = CheckStatus.Pass)
    {
        var check = new FakeCheck(name, status);
        _environment.Checks.Add(new CheckEntry(check));
        return check;
    }

    /// <summary>
    /// Adds a check that gives advice. The window reads these checks, and not
    /// PreflightRunner.AllPassed, before it takes the lobby path.
    /// </summary>
    private FakeCheck AddAdvisoryCheck(string name, CheckStatus status = CheckStatus.Pass)
    {
        var check = new FakeCheck(name, status) { Blocks = false };
        _environment.Checks.Add(new CheckEntry(check));
        return check;
    }

    /// <summary>
    /// Opens the lobby path: one check that blocks and passes, one check that
    /// gives advice and passes, and a launcher that can run. It returns the
    /// advisory check, so a test can make that one fail.
    /// </summary>
    private FakeCheck OpenTheLobbyPath()
    {
        AddCheck("ZeroTier");
        var advisory = AddAdvisoryCheck("DirectPlay registration");
        _environment.Launcher.CanLaunch = true;
        return advisory;
    }

    private LobbyViewModel Build()
    {
        if (_environment.Checks.Count == 0) AddCheck("ZeroTier");

        return new LobbyViewModel(_environment, _host);
    }

    /// <summary>Delivers one announcement to the transport of the running lobby.</summary>
    private void DeliverPeer(
        string nodeId = "bb11223344",
        string player = "chris",
        string address = "10.147.17.50",
        bool hosting = false,
        string appVersion = "1.0.0.0")
        => _environment.Transports[^1].Deliver(
            new Announcement
            {
                AppVersion = appVersion,
                NodeId = nodeId,
                Player = player,
                Address = address,
                Hosting = hosting,
            }.ToBytes(),
            IPAddress.Parse(address));

    // --- The order of the start ------------------------------------------

    [Fact]
    public async Task The_window_runs_the_checks_before_it_builds_the_lobby()
    {
        var seen = -1;
        var check = AddCheck("ZeroTier");
        check.OnRun = () => seen = _environment.CreateLobbyCount;

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal(0, seen);
        Assert.Equal(1, _environment.CreateLobbyCount);
    }

    [Fact]
    public async Task A_check_that_fails_keeps_the_lobby_closed()
    {
        AddCheck("ZeroTier", CheckStatus.Fail);

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal(0, _environment.CreateLobbyCount);
        Assert.False(model.CanHost);
        Assert.False(model.CanJoin);
        Assert.True(model.SetupExpanded);
    }

    [Fact]
    public async Task Every_check_that_passes_starts_the_lobby()
    {
        AddCheck("ZeroTier");
        AddCheck("DirectPlay");

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal(1, _environment.CreateLobbyCount);
        Assert.True(model.CanHost);
        Assert.False(model.SetupExpanded);
    }

    [Fact]
    public async Task An_advisory_check_that_fails_still_starts_the_lobby()
    {
        // The app raced before these two checks existed. A player who cannot
        // change the registry must keep that race.
        AddCheck("ZeroTier");
        var advisory = new FakeCheck("Compatibility mode", CheckStatus.Fail) { Blocks = false };
        _environment.Checks.Add(new CheckEntry(advisory));

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal(1, _environment.CreateLobbyCount);
        Assert.True(model.CanHost);
    }

    [Fact]
    public async Task The_window_shows_the_row_of_an_advisory_check_that_fails()
    {
        // The row, the reason and the Fix button all stay. Only the gate on
        // Host and Join lets this check go.
        AddCheck("ZeroTier");
        var advisory = new FakeCheck("Compatibility mode", CheckStatus.Fail)
        {
            Blocks = false,
            Message = "The game runs in the WINXPSP2 compatibility mode.",
            FixDescription = "The fix removes the mode.",
        };
        _environment.Checks.Add(new CheckEntry(advisory));

        await using var model = Build();
        await model.StartAsync();

        var row = model.CheckRows.Single(one => one.Name == "Compatibility mode");

        Assert.Equal("Optional", row.Mark);
        Assert.Equal("The game runs in the WINXPSP2 compatibility mode.", row.Message);
        Assert.Equal("The fix removes the mode.", row.Advice);
        Assert.True(row.ShowAction);
    }

    [Fact]
    public async Task The_setup_list_stays_open_for_an_advisory_check_that_fails()
    {
        // The panel closes when every check passes. Without this rule the
        // panel would close over the row, and no player would ever read the
        // one thing that the app offers them.
        AddCheck("ZeroTier");
        var advisory = new FakeCheck("Compatibility mode", CheckStatus.Fail) { Blocks = false };
        _environment.Checks.Add(new CheckEntry(advisory));

        await using var model = Build();
        await model.StartAsync();

        Assert.True(model.SetupExpanded);
    }

    [Fact]
    public async Task The_setup_list_holds_a_row_for_every_check()
    {
        // A check that passes keeps its row, because the firewall row offers a
        // repair while it passes.
        AddCheck("ZeroTier");
        AddCheck("DirectPlay", CheckStatus.Fail);

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal(new[] { "ZeroTier", "DirectPlay" }, model.CheckRows.Select(row => row.Name));
    }

    [Fact]
    public async Task The_setup_list_offers_Repair_beside_a_check_that_passes_with_a_hint()
    {
        var check = new FakeCheck("Windows Firewall");
        _environment.Checks.Add(new CheckEntry(check, "The rule can name an old game folder."));

        await using var model = Build();
        await model.StartAsync();

        var row = Assert.Single(model.CheckRows);
        Assert.True(row.ShowAction);
        Assert.Equal("Repair", row.ActionLabel);
    }

    [Fact]
    public async Task A_start_that_fails_shows_why_and_keeps_the_buttons_off()
    {
        AddCheck("ZeroTier");
        _environment.CreateTransport = () => new FailingTransport();

        await using var model = Build();
        await model.StartAsync();

        Assert.False(model.CanHost);
        Assert.False(model.CanJoin);
        Assert.Contains("the port is busy", model.StatusMessage);
    }

    [Fact]
    public async Task The_player_can_check_again_after_a_start_that_fails()
    {
        // LobbyService.StartAsync accepts one call, so the window must build a
        // new service for the second attempt.
        AddCheck("ZeroTier");
        _environment.CreateTransport = () => new FailingTransport();

        await using var model = Build();
        await model.StartAsync();

        _environment.CreateTransport = null;
        await model.CheckAgainAsync();

        Assert.Equal(2, _environment.CreateLobbyCount);
        Assert.True(model.CanHost);

        // The message belongs to the attempt that ended. A running lobby and
        // "The lobby did not start" on the screen at once tell the player two
        // different things.
        Assert.Equal(string.Empty, model.StatusMessage);
        Assert.False(model.HasStatusMessage);
    }

    [Fact]
    public async Task The_window_does_not_ask_for_an_address_that_it_lacks()
    {
        // MyAddress is null until a cycle reads the ZeroTier network.
        AddCheck("ZeroTier");
        ((FakeZeroTierClient)_environment.ZeroTier).Network = null;

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();

        Assert.Equal(string.Empty, model.HostAddress);
        Assert.DoesNotContain("Give the address", model.HostTitle);
        Assert.Contains("You host this race", model.HostTitle);

        // A cycle fills the address in. The player must know that waiting is
        // the whole of the work.
        Assert.Contains("Wait", model.HostTitle);
    }

    [Fact]
    public async Task A_repair_that_works_closes_the_setup_list_and_keeps_its_report()
    {
        // The window offers the firewall Repair only while every check
        // passes. The checks pass again after that repair, and the setup
        // list then closes itself. So the window must show FixMessage
        // outside that list.
        var check = new FakeCheck("Windows Firewall");
        _environment.Checks.Add(new CheckEntry(check, "The rule can name an old game folder."));
        check.OnFix = () => check.Message = "Windows accepted the commands.";

        await using var model = Build();
        await model.StartAsync();
        await model.FixAsync("Windows Firewall");

        Assert.False(model.SetupExpanded);
        Assert.True(model.HasFixMessage);
        Assert.Contains("Windows accepted the commands", model.FixMessage);
    }

    [Fact]
    public async Task The_player_can_close_the_setup_list()
    {
        // Expander.IsExpanded binds both ways. A setter that refuses the write
        // would leave the panel and the view model with different answers.
        AddCheck("ZeroTier", CheckStatus.Fail);

        await using var model = Build();
        await model.StartAsync();

        Assert.True(model.SetupExpanded);

        model.SetupExpanded = false;

        Assert.False(model.SetupExpanded);
    }

    // --- Check again -------------------------------------------------------

    [Fact]
    public async Task Check_again_disposes_the_service_and_builds_a_new_one()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();
        await model.CheckAgainAsync();

        Assert.Equal(2, _environment.CreateLobbyCount);
        Assert.Equal(2, _environment.Transports.Count);
        Assert.True(_environment.Transports[0].Disposed);
        Assert.Equal(LobbyService.Port, _environment.Transports[1].StartedPort);
    }

    [Fact]
    public async Task Check_again_runs_every_check_again()
    {
        var check = AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();
        await model.CheckAgainAsync();

        Assert.Equal(2, check.RunCalls);
    }

    [Fact]
    public async Task Check_again_stops_the_old_repeat()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();
        await model.CheckAgainAsync();

        Assert.Equal(2, _host.RepeatCount);
        Assert.Equal(1, _host.RepeatStopCount);
    }

    // --- The fix buttons ---------------------------------------------------

    [Fact]
    public async Task Fix_runs_the_fix_of_the_named_check_only()
    {
        var zeroTier = AddCheck("ZeroTier", CheckStatus.Fail);
        var directPlay = AddCheck("DirectPlay", CheckStatus.Fail);

        await using var model = Build();
        await model.StartAsync();
        await model.FixAsync("DirectPlay");

        Assert.Equal(0, zeroTier.FixCalls);
        Assert.Equal(1, directPlay.FixCalls);
    }

    [Fact]
    public async Task A_fix_that_works_runs_the_checks_again_and_starts_the_lobby()
    {
        var check = AddCheck("ZeroTier", CheckStatus.Fail);
        check.OnFix = () => check.Status = CheckStatus.Pass;

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal(0, _environment.CreateLobbyCount);

        await model.FixAsync("ZeroTier");

        Assert.Equal(1, _environment.CreateLobbyCount);
        Assert.True(model.CanHost);
    }

    [Fact]
    public async Task A_fix_that_throws_reaches_the_player()
    {
        var check = AddCheck("ZeroTier", CheckStatus.Fail);
        check.FixFault = new IOException("the disk is full");

        await using var model = Build();
        await model.StartAsync();
        await model.FixAsync("ZeroTier");

        Assert.Contains("the disk is full", model.FixMessage);
        Assert.True(model.HasFixMessage);
    }

    [Fact]
    public async Task The_window_shows_what_the_fix_reported()
    {
        // The checks run again after a fix, and the rows then hold the text of
        // RunAsync. Only this message names the exit code of netsh, or the
        // restart that DISM needs.
        var check = AddCheck("Windows Firewall", CheckStatus.Fail);
        check.OnFix = () =>
        {
            check.Message = "netsh did not add the inbound rule (exit code 127).";
            check.FixDescription = "Open an administrator command prompt and run netsh.";
        };

        await using var model = Build();
        await model.StartAsync();
        await model.FixAsync("Windows Firewall");

        Assert.Contains("exit code 127", model.FixMessage);
        Assert.Contains("administrator command prompt", model.FixMessage);
    }

    [Fact]
    public async Task Check_again_clears_what_the_last_fix_reported()
    {
        var check = AddCheck("Windows Firewall", CheckStatus.Fail);
        check.OnFix = () => check.Message = "netsh did not add the inbound rule.";

        await using var model = Build();
        await model.StartAsync();
        await model.FixAsync("Windows Firewall");

        Assert.True(model.HasFixMessage);

        await model.CheckAgainAsync();

        Assert.Equal(string.Empty, model.FixMessage);
        Assert.False(model.HasFixMessage);
    }

    [Fact]
    public async Task A_fix_keeps_the_buttons_off_until_the_lobby_starts()
    {
        // Busy stays true from the start of the fix to the end of the start.
        // A gap would let a second click run two lobby services at once.
        var seen = new List<bool>();
        var check = AddCheck("ZeroTier", CheckStatus.Fail);
        check.OnFix = () => check.Status = CheckStatus.Pass;

        await using var model = Build();
        check.OnRun = () => seen.Add(model.CanAct);

        await model.StartAsync();
        await model.FixAsync("ZeroTier");

        Assert.NotEmpty(seen);
        Assert.All(seen, canAct => Assert.False(canAct));
        Assert.True(model.CanAct);
    }

    [Fact]
    public async Task The_window_says_that_it_is_at_work()
    {
        // dism takes tens of seconds. A button that the window disables tells
        // the player nothing about the reason.
        string? duringTheFix = null;
        var check = AddCheck("DirectPlay", CheckStatus.Fail);

        await using var model = Build();
        check.OnFix = () => duringTheFix = model.StatusMessage;

        await model.StartAsync();
        await model.FixAsync("DirectPlay");

        Assert.NotNull(duringTheFix);
        Assert.Contains("busy", duringTheFix);
        Assert.Equal(string.Empty, model.StatusMessage);
    }

    [Fact]
    public async Task The_window_stays_busy_through_the_checks_that_follow_a_fix()
    {
        // A fix ends, and then the window runs every check again, which runs
        // netsh and zerotier-cli. The buttons are off for all of that time,
        // so the window must still say why.
        var seen = new List<string>();
        var check = AddCheck("DirectPlay", CheckStatus.Fail);

        await using var model = Build();
        check.OnRun = () => seen.Add(model.StatusMessage);

        await model.StartAsync();
        await model.FixAsync("DirectPlay");

        // The last run of the check is the one that follows the fix.
        Assert.Contains("busy", seen[^1]);
        Assert.Equal(string.Empty, model.StatusMessage);
    }

    [Fact]
    public async Task Fix_ignores_a_name_that_no_check_carries()
    {
        var check = AddCheck("ZeroTier", CheckStatus.Fail);

        await using var model = Build();
        await model.StartAsync();
        await model.FixAsync("Nothing");

        Assert.Equal(0, check.FixCalls);
    }

    // --- The peer list -----------------------------------------------------

    [Fact]
    public async Task The_window_moves_PeersChanged_to_the_user_interface_thread()
    {
        // The service raises the event on the pump thread. A binding that
        // another thread touches throws.
        AddCheck("ZeroTier");
        _host.RunPostedWork = false;

        await using var model = Build();
        await model.StartAsync();

        Assert.True(_host.PostCount > 0);
        Assert.Empty(model.PeerRows);

        _host.RunPost();
        Assert.True(_host.PostCount > 0);
    }

    [Fact]
    public async Task The_lobby_list_shows_one_row_for_every_peer()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer();
        await _environment.Lobbies[0].RunCycleAsync(default);

        var row = Assert.Single(model.PeerRows);
        Assert.Equal("chris", row.Name);
        Assert.Equal("In the lobby", row.State);
    }

    [Fact]
    public async Task The_lobby_list_marks_a_peer_that_runs_another_version()
    {
        AddCheck("ZeroTier");
        _environment.AppVersion = "1.0.0.0";

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(appVersion: "0.9.0.0");
        await _environment.Lobbies[0].RunCycleAsync(default);

        var row = Assert.Single(model.PeerRows);
        Assert.True(row.VersionDiffers);
    }

    // --- The host and the steps -------------------------------------------

    [Fact]
    public async Task The_window_shows_its_own_address_while_it_hosts()
    {
        // CurrentHost never returns this machine. A window that reads
        // CurrentHost only would show "no host" while this player hosts.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();

        Assert.Equal("10.147.17.42", model.HostAddress);
        Assert.Contains("You host", model.HostTitle);
        Assert.Null(_environment.Lobbies[0].CurrentHost);
    }

    [Fact]
    public async Task The_window_shows_the_steps_for_the_host()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();

        Assert.Equal(JoinInstructions.ForHost(), model.JoinSteps);
    }

    [Fact]
    public async Task The_window_shows_the_steps_of_a_peer_that_joins()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        Assert.Equal(JoinInstructions.For("10.147.17.50"), model.JoinSteps);
        Assert.Equal("10.147.17.50", model.HostAddress);
        Assert.Contains("chris", model.HostTitle);
    }

    [Fact]
    public async Task The_window_shows_no_steps_while_no_peer_hosts()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        Assert.Empty(model.JoinSteps);
        Assert.Equal(string.Empty, model.HostAddress);
        Assert.Contains("No one hosts", model.HostTitle);
    }

    [Fact]
    public async Task The_steps_stay_the_same_while_the_host_stays_the_same()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        var changes = 0;
        model.JoinSteps.CollectionChanged += (_, _) => changes++;

        _host.Tick();
        _host.Tick();

        Assert.Equal(0, changes);
    }

    // --- The buttons -------------------------------------------------------

    [Fact]
    public async Task Join_is_off_while_no_peer_hosts()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        Assert.True(model.CanHost);
        Assert.False(model.CanJoin);
    }

    [Fact]
    public async Task Join_is_on_while_a_peer_hosts()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        Assert.True(model.CanJoin);
    }

    [Fact]
    public async Task Join_is_off_while_this_app_hosts()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.HostAsync();

        Assert.False(model.CanJoin);
    }

    [Fact]
    public async Task The_host_button_offers_to_stop_the_race_while_this_app_hosts()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal("Host a race", model.HostButtonText);

        await model.HostAsync();
        Assert.Equal("Stop the race", model.HostButtonText);

        await model.HostAsync();
        Assert.Equal("Host a race", model.HostButtonText);
    }

    [Fact]
    public async Task A_second_press_stops_the_race()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        await model.HostAsync();

        Assert.False(_environment.Lobbies[0].IsHosting);
    }

    [Fact]
    public async Task Join_copies_the_address()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();

        Assert.Equal("10.147.17.50", Assert.Single(_host.CopiedText));
    }

    [Fact]
    public async Task Join_reports_a_clipboard_that_fails()
    {
        AddCheck("ZeroTier");
        _host.CopyFault = new InvalidOperationException("no clipboard");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();

        // The clipboard is a convenience. The window also shows the address in
        // a large font, so the message sends the player to that address.
        Assert.Contains("no clipboard", model.StatusMessage);
        Assert.Contains("Read the address above", model.StatusMessage);
    }

    [Fact]
    public async Task The_window_shows_that_the_game_did_not_start()
    {
        AddCheck("ZeroTier");
        _environment.LaunchFault = new IOException("midtown2.exe is missing");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Contains("midtown2.exe is missing", model.StatusMessage);
    }

    // --- The launch button -------------------------------------------------

    [Fact]
    public async Task Host_announces_the_race_and_starts_no_game()
    {
        // MM2 covers the screen. A game that starts here hides the address
        // that the host must give to the others.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();

        Assert.True(_environment.Lobbies[0].IsHosting);
        Assert.Empty(_environment.LaunchedPaths);
    }

    [Fact]
    public async Task Host_copies_the_address_of_this_app()
    {
        // The host pastes the address to the others before the game starts.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();

        Assert.Equal("10.147.17.42", Assert.Single(_host.CopiedText));
    }

    [Fact]
    public async Task Host_reports_a_clipboard_that_fails_and_keeps_the_race()
    {
        AddCheck("ZeroTier");
        _host.CopyFault = new InvalidOperationException("no clipboard");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();

        Assert.Contains("no clipboard", model.StatusMessage);
        Assert.Contains("Read the address above", model.StatusMessage);

        // The clipboard is a convenience. The race goes on without it.
        Assert.True(_environment.Lobbies[0].IsHosting);
        Assert.True(model.CanStartGame);
    }

    [Fact]
    public async Task Join_starts_no_game()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();

        Assert.Empty(_environment.LaunchedPaths);
    }

    [Fact]
    public async Task The_launch_button_waits_for_Host_or_Join()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        Assert.True(model.CanHost);
        Assert.False(model.CanStartGame);

        await model.HostAsync();

        Assert.True(model.CanStartGame);
    }

    [Fact]
    public async Task A_press_before_Host_or_Join_starts_no_game()
    {
        // This method starts a process. It carries the gate of Host and Join,
        // so a click that arrives late starts nothing.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        model.StartGame();

        Assert.Empty(_environment.LaunchedPaths);
    }

    [Fact]
    public async Task The_launch_button_names_the_race_for_the_host()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();

        Assert.Equal("Start the race", model.StartGameButtonText);
    }

    [Fact]
    public async Task The_launch_button_names_the_game_for_a_player_who_joins()
    {
        // A player who joins does not start a race. Another player does.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();

        Assert.Equal("Start the game", model.StartGameButtonText);
    }

    [Fact]
    public async Task The_launch_button_starts_the_game_at_the_path_of_the_one_configuration()
    {
        AddCheck("ZeroTier");
        _environment.Config.GamePath = @"D:\MM2\midtown2.exe";

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Equal(@"D:\MM2\midtown2.exe", Assert.Single(_environment.LaunchedPaths));
        Assert.Contains("MM2 started", model.StatusMessage);
    }

    [Fact]
    public async Task A_second_press_of_Host_takes_the_launch_button_away()
    {
        // The second press ends the race. A live button would then start MM2
        // for a race that nobody hosts.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        await model.HostAsync();

        Assert.False(model.CanStartGame);
    }

    [Fact]
    public async Task Host_after_Join_makes_this_player_the_host()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();
        await model.HostAsync();

        Assert.True(model.CanStartGame);
        Assert.Equal("Start the race", model.StartGameButtonText);
    }

    [Fact]
    public async Task A_host_that_stops_takes_the_launch_button_from_a_player_who_joined()
    {
        // The address of that host belongs to a race that ended. A live button
        // promises a game that no host answers.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();

        Assert.True(model.CanStartGame);

        DeliverPeer(hosting: false);
        await _environment.Lobbies[0].RunCycleAsync(default);

        Assert.False(model.CanStartGame);
        Assert.Contains("The host stopped the race", model.StatusMessage);
    }

    [Fact]
    public async Task The_launch_button_stays_on_after_a_launch()
    {
        // MM2 is flaky. The host must be able to start it again, and he must
        // keep his place in the lobby.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();
        model.StartGame();

        Assert.Equal(2, _environment.LaunchedPaths.Count);
        Assert.True(model.CanStartGame);
    }

    [Fact]
    public async Task Check_again_takes_the_launch_button_away()
    {
        // Check again builds a new lobby service, and a new service hosts
        // nothing. The player presses Host again.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        await model.CheckAgainAsync();

        Assert.True(model.CanHost);
        Assert.False(model.CanStartGame);
    }

    [Fact]
    public async Task A_new_network_ID_takes_the_launch_button_away()
    {
        // The checks ran against the old ID, and those checks gate every
        // button that starts work.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();

        Assert.True(model.CanStartGame);

        model.ZeroTierNetworkId = "8056c2e21c000001";

        Assert.False(model.CanStartGame);
    }

    // --- The name editor ---------------------------------------------------

    [Fact]
    public async Task The_name_editor_writes_to_the_one_configuration()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        model.DisplayName = "dallin";

        Assert.Equal("dallin", _environment.Config.DisplayName);
    }

    [Fact]
    public async Task The_next_announcement_carries_the_new_name()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        model.DisplayName = "dallin";
        await _environment.Lobbies[0].RunCycleAsync(default);

        var announcement = Announcement.TryParse(_environment.Transports[0].Sent[^1].Payload);
        Assert.NotNull(announcement);
        Assert.Equal("dallin", announcement.Player);
    }

    [Fact]
    public async Task The_name_editor_stops_at_the_limit_of_PeerTable()
    {
        // PeerTable rejects a longer name. Then every friend stops showing
        // this player, and the player cannot see that.
        AddCheck("ZeroTier");
        await using var model = Build();

        Assert.Equal(PeerTable.MaxPlayerNameLength, model.DisplayNameMaxLength);
    }

    [Fact]
    public async Task The_window_shortens_a_name_that_is_too_long()
    {
        AddCheck("ZeroTier");
        var tooLong = new string('a', PeerTable.MaxPlayerNameLength + 8);

        await using var model = Build();
        model.DisplayName = tooLong;

        Assert.Equal(PeerTable.MaxPlayerNameLength, model.DisplayName.Length);
        Assert.Equal(PeerTable.MaxPlayerNameLength, _environment.Config.DisplayName.Length);
    }

    [Fact]
    public async Task The_window_shortens_a_name_that_the_file_holds()
    {
        AddCheck("ZeroTier");
        _environment.Config.DisplayName = new string('a', PeerTable.MaxPlayerNameLength + 8);

        await using var model = Build();

        Assert.Equal(PeerTable.MaxPlayerNameLength, _environment.Config.DisplayName.Length);
    }

    [Fact]
    public async Task The_name_editor_saves_the_configuration()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        model.DisplayName = "dallin";

        Assert.Equal(1, _environment.SaveCount);
        Assert.Equal("dallin", _environment.ConfigStore.Load().DisplayName);
    }

    [Fact]
    public async Task A_save_that_fails_reaches_the_player()
    {
        AddCheck("ZeroTier");
        _environment.SaveFault = new IOException("the file is read-only");

        await using var model = Build();
        model.DisplayName = "dallin";

        Assert.Contains("the file is read-only", model.StatusMessage);
    }

    /// <summary>
    /// The name that the app announces for the computer name. PeerTable also
    /// limits the length, so a long computer name arrives short.
    /// </summary>
    private static string ComputerName() => string.Concat(
        Environment.MachineName.Take(PeerTable.MaxPlayerNameLength));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task The_window_puts_the_computer_name_in_the_place_of_a_blank_name(string blank)
    {
        // PeerTable rejects an announcement that carries no name. Every friend
        // then stops showing this player, and the player cannot see that,
        // because the app excludes itself from its own list.
        AddCheck("ZeroTier");

        await using var model = Build();
        model.DisplayName = blank;

        Assert.Equal(ComputerName(), model.DisplayName);
        Assert.Equal(ComputerName(), _environment.Config.DisplayName);
    }

    [Fact]
    public async Task A_player_who_clears_the_name_box_stays_visible_to_every_friend()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        model.DisplayName = string.Empty;
        await _environment.Lobbies[0].RunCycleAsync(default);

        // Every friend runs this test on the datagram. A datagram that carries
        // no name does not reach their list. This assertion comes first,
        // because it is the one that the defect broke.
        var payload = _environment.Transports[0].Sent[^1].Payload;
        var address = IPAddress.Parse("10.147.17.42");
        var table = new PeerTable(LobbyService.PeerExpiry, () => _environment.Now);
        Assert.True(table.Accept(payload, address, IPv4Math.Subnet(address, 24)));

        var announcement = Announcement.TryParse(payload);
        Assert.NotNull(announcement);
        Assert.Equal(ComputerName(), announcement.Player);
    }

    // --- The network ID editor ---------------------------------------------

    [Fact]
    public async Task The_window_shows_the_network_ID_of_the_configuration()
    {
        AddCheck("ZeroTier");
        _environment.Config.ZeroTierNetworkId = "8056c2e21c000001";

        await using var model = Build();

        Assert.Equal("8056c2e21c000001", model.ZeroTierNetworkId);
    }

    [Fact]
    public async Task The_ID_editor_writes_the_ID_to_the_one_configuration()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        model.ZeroTierNetworkId = "   8056C2E21C000001   ";

        Assert.Equal("8056c2e21c000001", _environment.Config.ZeroTierNetworkId);
        Assert.Equal("8056c2e21c000001", model.ZeroTierNetworkId);
        Assert.Equal(1, _environment.SaveCount);
        Assert.Equal("8056c2e21c000001", _environment.ConfigStore.Load().ZeroTierNetworkId);
    }

    [Fact]
    public async Task The_window_names_Check_again_after_it_saves_an_ID()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        model.ZeroTierNetworkId = "8056c2e21c000001";

        Assert.True(model.HasNetworkIdMessage);
        Assert.Contains("Check again", model.NetworkIdMessage);
    }

    [Fact]
    public async Task A_bad_ID_does_not_reach_the_configuration()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        model.ZeroTierNetworkId = "my network";

        // The configuration keeps the ID that it had, and the box keeps the
        // text of the player, so the player can correct that text.
        Assert.Equal("0123456789abcdef", _environment.Config.ZeroTierNetworkId);
        Assert.Equal(0, _environment.SaveCount);
        Assert.Equal("my network", model.ZeroTierNetworkId);
    }

    [Fact]
    public async Task The_advice_for_a_bad_ID_names_the_rule_and_ZeroTier_Central()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        model.ZeroTierNetworkId = "abc";

        Assert.True(model.HasNetworkIdMessage);
        Assert.Contains("16 characters", model.NetworkIdMessage);
        Assert.Contains("https://my.zerotier.com", model.NetworkIdMessage);
    }

    [Fact]
    public async Task The_window_saves_nothing_when_the_ID_does_not_change()
    {
        AddCheck("ZeroTier");

        await using var model = Build();

        // The binding writes on every loss of the focus. The same ID must not
        // write the file again.
        model.ZeroTierNetworkId = "0123456789ABCDEF";

        Assert.Equal(0, _environment.SaveCount);
        Assert.Equal("0123456789abcdef", model.ZeroTierNetworkId);
        Assert.False(model.HasNetworkIdMessage);
    }

    [Fact]
    public async Task A_new_network_ID_marks_every_check_row_stale()
    {
        // The checks ran against the old ID. A row that still says OK asserts
        // something that no longer holds, and the ZeroTier row is the one that
        // the new ID can turn into a problem.
        AddCheck("ZeroTier");
        AddCheck("Firewall");

        await using var model = Build();
        await model.StartAsync();

        Assert.All(model.CheckRows, row => Assert.Equal("OK", row.Mark));
        Assert.False(model.SetupExpanded);

        model.ZeroTierNetworkId = "8056c2e21c000001";

        Assert.Equal(
            new[] { "ZeroTier", "Firewall" },
            model.CheckRows.Select(row => row.Name));

        Assert.All(model.CheckRows, row =>
        {
            Assert.Equal("Unknown", row.Mark);
            Assert.Contains("Check again", row.Message);
            Assert.False(row.ShowAction);
        });

        // The Check again button sits inside the setup panel. A closed panel
        // hides the button that the message names.
        Assert.True(model.SetupExpanded);
    }

    [Fact]
    public async Task A_new_network_ID_takes_Host_and_Join_away()
    {
        // The ZeroTier check gates the two buttons, and that check reads the
        // network ID. A new ID therefore makes the gate unknown. A live Host
        // button would promise a race that the app cannot give: a service that
        // cannot find the new network sends no announcement, and no friend
        // sees the race.
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        Assert.True(model.CanHost);
        Assert.True(model.CanJoin);

        model.ZeroTierNetworkId = "8056c2e21c000001";

        Assert.False(model.CanHost);
        Assert.False(model.CanJoin);

        // Check again is the way back.
        await model.CheckAgainAsync();

        Assert.True(model.CanHost);
    }

    [Fact]
    public async Task The_same_network_ID_leaves_the_check_rows_alone()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        model.ZeroTierNetworkId = "0123456789ABCDEF";

        Assert.Equal("OK", model.CheckRows[0].Mark);
        Assert.False(model.SetupExpanded);
    }

    [Fact]
    public async Task A_bad_network_ID_leaves_the_check_rows_alone()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        model.ZeroTierNetworkId = "my network";

        // The configuration keeps the old ID, so every result still holds.
        Assert.Equal("OK", model.CheckRows[0].Mark);
    }

    [Fact]
    public async Task A_player_who_types_the_ID_reaches_the_lobby_without_a_restart()
    {
        // A fresh install holds no network ID. The ZeroTier check then fails,
        // and StartTheLobbyAsync returns before it builds a lobby. This test
        // walks the way out of that state: the player types the ID, and then
        // chooses Check again.
        var zeroTier = new FakeZeroTierClient();
        _environment.ZeroTier = zeroTier;
        _environment.Config.ZeroTierNetworkId = string.Empty;
        _environment.Checks.Add(
            new CheckEntry(new ZeroTierCheck(
                zeroTier,
                _environment.Config,
                new ZeroTierFaultReader(
                    new FakeProcessRunner(),
                    new ZeroTierCommand("zerotier-cli", Array.Empty<string>())),
                new ZeroTierTokenGrant(
                    new FakeElevatedProcessRunner(), () => null, () => "shared"))));

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal(0, _environment.CreateLobbyCount);
        Assert.False(model.CanHost);

        model.ZeroTierNetworkId = " 0123456789ABCDEF ";
        await model.CheckAgainAsync();

        Assert.Equal(1, _environment.CreateLobbyCount);
        Assert.True(model.CanHost);
    }

    // --- The fault of the last cycle ---------------------------------------

    [Fact]
    public async Task The_window_reads_the_state_once_every_cycle_period()
    {
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal(LobbyService.CyclePeriod, _host.RepeatPeriod);
        Assert.Equal(1, _host.RepeatCount);
    }

    [Fact]
    public async Task The_window_shows_the_exception_of_the_last_failed_cycle()
    {
        // The service raises PeersChanged only after a cycle that completes.
        // So the window must read LastCycleFault on its own.
        var client = new ScriptedZeroTierClient
        {
            FailOn = call => call >= 2 ? new IOException("zerotier-cli is gone") : null,
        };

        _environment.ZeroTier = client;
        AddCheck("ZeroTier");

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal(string.Empty, model.FaultMessage);

        _environment.Time.Advance(LobbyService.CyclePeriod);
        await client.WhenCalled(2).WaitAsync(WaitLimit);
        _environment.Time.Advance(LobbyService.CyclePeriod);
        await client.WhenCalled(3).WaitAsync(WaitLimit);

        _host.Tick();

        Assert.Contains("zerotier-cli is gone", model.FaultMessage);
        Assert.True(model.HasFaultMessage);

        // A friend reads this line and repeats it. "Refresh" is the word that
        // a friend knows. "Cycle" is the word of the code.
        Assert.Contains("refresh", model.FaultMessage);
    }

    // --- The close ---------------------------------------------------------

    [Fact]
    public async Task Dispose_stops_the_lobby_service()
    {
        AddCheck("ZeroTier");

        var model = Build();
        await model.StartAsync();
        await model.DisposeAsync();

        Assert.True(_environment.Transports[0].Disposed);
        Assert.Equal(1, _host.RepeatStopCount);
    }

    [Fact]
    public async Task Dispose_accepts_a_second_call()
    {
        AddCheck("ZeroTier");

        var model = Build();
        await model.StartAsync();
        await model.DisposeAsync();
        await model.DisposeAsync();

        Assert.Single(_environment.Transports);
    }

    // --- The lobby launch ---------------------------------------------------

    [Fact]
    public async Task The_launch_button_asks_the_lobby_to_start_the_race_for_the_host()
    {
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        var request = Assert.Single(_environment.Launcher.Requests);
        Assert.Equal(LobbyRole.Host, request.Role);
        Assert.Null(request.HostAddress);
    }

    [Fact]
    public async Task The_launch_button_asks_the_lobby_to_join_the_address_that_the_app_knows()
    {
        // The app reads the address from the announcement of the host. So a
        // player who joins types no address at any point.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();
        model.StartGame();

        var request = Assert.Single(_environment.Launcher.Requests);
        Assert.Equal(LobbyRole.Join, request.Role);
        Assert.Equal("10.147.17.50", request.HostAddress);
    }

    [Fact]
    public async Task The_lobby_launch_carries_the_name_of_the_player()
    {
        OpenTheLobbyPath();
        _environment.Config.DisplayName = "dallin";

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Equal("dallin", Assert.Single(_environment.Launcher.Requests).PlayerName);
    }

    [Fact]
    public async Task A_lobby_launch_that_works_starts_no_second_game()
    {
        // DirectPlay started the game. A second start would put two copies of
        // MM2 on the screen, and one of them would sit in no race.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Empty(_environment.LaunchedPaths);
    }

    [Fact]
    public async Task A_lobby_launch_that_works_says_that_the_game_is_in_the_race()
    {
        // The window shows the sentence of the launcher, and not the sentence
        // of the ordinary start. "MM2 started. Follow the steps above." would
        // send the player to steps that he does not need.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Equal("MM2 Lobby started the game in the race.", model.StatusMessage);
    }

    [Fact]
    public async Task A_lobby_launch_that_works_tells_the_host_that_he_types_no_address()
    {
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Equal(JoinInstructions.ForTheHostInTheRace(), model.JoinSteps);
    }

    [Fact]
    public async Task A_lobby_launch_that_works_tells_a_player_who_joined_that_he_types_no_address()
    {
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();
        model.StartGame();

        Assert.Equal(JoinInstructions.ForThePlayerInTheRace(), model.JoinSteps);
    }

    [Fact]
    public async Task The_window_never_asks_a_player_in_the_race_to_type_an_address()
    {
        // This is the defect that this project met five times: a screen that
        // asserts something that the path did not do. The lobby gave the
        // address to the game, so no step may ask the player for it.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();
        model.StartGame();

        Assert.NotEmpty(model.JoinSteps);
        Assert.DoesNotContain(model.JoinSteps, step => step.Contains("10.147.17.50"));
        Assert.DoesNotContain(model.JoinSteps, step => step.Contains("Type"));
    }

    [Fact]
    public async Task The_steps_of_a_lobby_launch_hold_through_the_cycles_that_follow()
    {
        // The window reads the state once every two seconds. Without one
        // source for the steps, that read would put the ordinary steps back
        // and ask the host for a menu that he never opened.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        _host.Tick();
        _host.Tick();

        Assert.Equal(JoinInstructions.ForTheHostInTheRace(), model.JoinSteps);
    }

    [Fact]
    public async Task The_window_still_shows_the_address_after_a_lobby_launch()
    {
        // The address is information and not an instruction. A friend whose
        // own lobby path is shut still needs it.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        // The refresh reads the state again. The address must survive it.
        _host.Tick();

        Assert.Equal("10.147.17.42", model.HostAddress);
    }

    [Fact]
    public async Task A_lobby_launch_that_fails_starts_the_game_the_ordinary_way()
    {
        OpenTheLobbyPath();
        _environment.Launcher.Fails(LobbyLaunchFault.DirectPlayRefused);

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Equal(@"C:\Games\MM2\midtown2.exe", Assert.Single(_environment.LaunchedPaths));
    }

    [Fact]
    public async Task A_lobby_launch_that_fails_shows_the_steps_of_the_ordinary_way()
    {
        // The player starts MM2 himself here, so he needs every step. This is
        // the state that every player had before the lobby path existed.
        OpenTheLobbyPath();
        _environment.Launcher.Fails(LobbyLaunchFault.DirectPlayRefused);

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();
        model.StartGame();

        Assert.Equal(JoinInstructions.For("10.147.17.50"), model.JoinSteps);
    }

    [Fact]
    public async Task A_lobby_launch_that_fails_names_the_reason_and_the_step_for_the_player()
    {
        OpenTheLobbyPath();
        _environment.Launcher.Fails(LobbyLaunchFault.GameNotRegistered);

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        var advice = LobbyLaunchMessages.For(LobbyLaunchFault.GameNotRegistered);

        // The game started. That sentence stands first, because it is the one
        // that answers the press of the player.
        Assert.StartsWith("MM2 started. Follow the steps above.", model.StatusMessage);
        Assert.Contains(advice.Message, model.StatusMessage);
        Assert.Contains(advice.PlayerAction, model.StatusMessage);
    }

    [Fact]
    public async Task A_launcher_that_throws_still_starts_the_game()
    {
        // The launcher promises to throw nothing. If it breaks that promise,
        // the player must still get his game.
        OpenTheLobbyPath();
        _environment.Launcher.Fault = new InvalidOperationException("the vtable is wrong");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Single(_environment.LaunchedPaths);
        Assert.StartsWith("MM2 started. Follow the steps above.", model.StatusMessage);
        Assert.Contains("the vtable is wrong", model.LaunchReport);
    }

    [Fact]
    public async Task The_window_makes_no_lobby_launch_while_an_advisory_check_does_not_pass()
    {
        // The row of that check already names the reason and offers the Fix.
        OpenTheLobbyPath().Status = CheckStatus.Fail;

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Empty(_environment.Launcher.Requests);
        Assert.Single(_environment.LaunchedPaths);
    }

    [Fact]
    public async Task The_window_makes_no_lobby_launch_while_one_of_two_advisory_checks_fails()
    {
        // Each advisory check names a condition of the lobby path. So every
        // one of them must pass, and not merely one of them.
        OpenTheLobbyPath();
        AddAdvisoryCheck("Compatibility mode", CheckStatus.Fail);

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Empty(_environment.Launcher.Requests);
        Assert.Single(_environment.LaunchedPaths);
    }

    [Fact]
    public async Task A_second_press_of_Join_takes_the_steps_of_the_race_away()
    {
        // The player announced himself again, and he has not pressed the
        // launch button since. The window must ask nothing of him that it
        // cannot prove, and it must not wait two seconds to stop asking.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();
        model.StartGame();
        await model.JoinAsync();

        Assert.Equal(JoinInstructions.For("10.147.17.50"), model.JoinSteps);
    }

    [Fact]
    public async Task The_window_makes_no_lobby_launch_while_the_launcher_refuses()
    {
        // A 64 bit copy of the app cannot reach DirectPlay. The player still
        // races: the app starts the game, and he types the address.
        OpenTheLobbyPath();
        _environment.Launcher.CanLaunch = false;

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Empty(_environment.Launcher.Requests);
        Assert.Single(_environment.LaunchedPaths);
        Assert.Equal(JoinInstructions.ForHost(), model.JoinSteps);
    }

    [Fact]
    public async Task A_launch_that_the_app_never_tried_says_nothing_about_the_lobby()
    {
        // The player reads the one sentence that the app gave him before this
        // path existed, and nothing more.
        OpenTheLobbyPath();
        _environment.Launcher.CanLaunch = false;

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Equal("MM2 started. Follow the steps above.", model.StatusMessage);
        Assert.False(model.HasLaunchReport);
    }

    [Fact]
    public async Task The_window_holds_the_session_of_a_lobby_launch()
    {
        // The session owns the DirectPlay interface and the memory that MM2
        // reads about three seconds after the launch. A release here sent four
        // runs of the spike back to the menus of the game.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        var session = Assert.Single(_environment.Launcher.Sessions);
        Assert.True(session.IsOpen);
        Assert.Equal(0, session.DisposeCount);
    }

    [Fact]
    public async Task The_window_closes_the_session_when_it_closes()
    {
        OpenTheLobbyPath();

        var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();
        await model.DisposeAsync();

        Assert.False(Assert.Single(_environment.Launcher.Sessions).IsOpen);
    }

    [Fact]
    public async Task A_second_press_closes_the_first_session_and_holds_one()
    {
        // MM2 stopped, so the player presses again. The app must not keep one
        // session for every press.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();
        model.StartGame();

        Assert.Equal(2, _environment.Launcher.Sessions.Count);
        Assert.False(_environment.Launcher.Sessions[0].IsOpen);
        Assert.True(_environment.Launcher.Sessions[1].IsOpen);
    }

    [Fact]
    public async Task The_window_closes_one_session_one_time_only()
    {
        // The window forgets a session that closed. Without that, it would
        // call into the same session on every press that follows.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        // The two presses below take no new session, so the first session is
        // the only one that the window can close.
        _environment.Launcher.Fails(LobbyLaunchFault.DirectPlayRefused);
        model.StartGame();
        model.StartGame();

        Assert.Equal(1, _environment.Launcher.Sessions[0].DisposeCount);
    }

    [Fact]
    public async Task A_press_while_the_game_runs_keeps_the_one_session()
    {
        // The session releases nothing while the game runs, because a release
        // there sends the player back to the menus. So the app takes no second
        // session.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        _environment.Launcher.Sessions[0].GameRuns = true;
        model.StartGame();

        Assert.Single(_environment.Launcher.Sessions);
        Assert.True(_environment.Launcher.Sessions[0].IsOpen);
    }

    [Fact]
    public async Task A_press_while_the_game_runs_starts_the_game_the_ordinary_way()
    {
        // The player presses, and he gets a game. The steps then name the
        // ordinary path, because that is the path that this press took.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        _environment.Launcher.Sessions[0].GameRuns = true;
        model.StartGame();

        Assert.Single(_environment.LaunchedPaths);
        Assert.Equal(JoinInstructions.ForHost(), model.JoinSteps);
    }

    [Fact]
    public async Task The_window_keeps_the_report_of_the_last_lobby_launch()
    {
        // The step of a failed launch asks the player to send a report. Only
        // this text holds the answer of each DirectPlay call.
        OpenTheLobbyPath();
        _environment.Launcher.Fails(LobbyLaunchFault.DirectPlayRefused, "RunApplication: 0x80004005");

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();

        Assert.Equal("RunApplication: 0x80004005", model.LaunchReport);
        Assert.True(model.HasLaunchReport);
    }

    [Fact]
    public async Task The_window_holds_no_report_before_a_launch()
    {
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        Assert.Equal(string.Empty, model.LaunchReport);
        Assert.False(model.HasLaunchReport);
    }

    [Fact]
    public async Task A_second_press_of_Host_takes_the_steps_of_the_race_away()
    {
        // The race ended. The game of the last launch belongs to that race.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();
        await model.HostAsync();

        Assert.Empty(model.JoinSteps);
    }

    [Fact]
    public async Task Check_again_takes_the_steps_of_the_race_away()
    {
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        await model.HostAsync();
        model.StartGame();
        await model.CheckAgainAsync();

        Assert.Empty(model.JoinSteps);
    }

    [Fact]
    public async Task A_host_that_stops_takes_the_steps_of_the_race_from_a_player_who_joined()
    {
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();
        model.StartGame();

        Assert.Equal(JoinInstructions.ForThePlayerInTheRace(), model.JoinSteps);

        DeliverPeer(hosting: false);
        await _environment.Lobbies[0].RunCycleAsync(default);

        Assert.Empty(model.JoinSteps);
    }

    [Fact]
    public async Task Host_after_a_lobby_launch_as_a_player_who_joined_takes_the_race_steps_away()
    {
        // The player joined through the lobby, and he then took the host role.
        // He must press the launch button again, and the window must ask
        // nothing of him until he does.
        OpenTheLobbyPath();

        await using var model = Build();
        await model.StartAsync();

        DeliverPeer(hosting: true);
        await _environment.Lobbies[0].RunCycleAsync(default);

        await model.JoinAsync();
        model.StartGame();
        await model.HostAsync();

        Assert.Equal(JoinInstructions.ForHost(), model.JoinSteps);
    }
}
