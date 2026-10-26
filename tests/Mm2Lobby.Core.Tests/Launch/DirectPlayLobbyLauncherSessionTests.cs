using System.Runtime.InteropServices;
using Mm2Lobby.Core.Launch;
using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Launch;

/// <summary>
/// These tests drive the launcher through the whole DirectPlay sequence, with a
/// fake Windows and a fake allocator. They prove the lifetime rule as the
/// launcher uses it.
/// <para>
/// NativeBlocks, KeptBlocks and LobbyLifetime each prove one half of that rule
/// on their own. These tests prove that the launcher asks them for the right
/// half: a successful launch keeps every block and releases nothing, and a
/// failed launch frees every block and releases the interface.
/// </para>
/// <para>
/// WARNING: a free after a successful launch sends the player back to the menus
/// of the game. MM2 asks DirectPlay for its settings three to four seconds
/// later, and DirectPlay reads these blocks at that moment. Four of the
/// fourteen diagnostic runs lost the race to that one bug.
/// </para>
/// </summary>
public class DirectPlayLobbyLauncherSessionTests : IDisposable
{
    private const string HostAddress = "10.147.17.42";

    private readonly FakeLobbyApi _api = new();
    private readonly RecordingNativeAllocator _allocator = new();

    private bool _gameRuns = true;

    public void Dispose() => _allocator.Dispose();

    [Fact]
    public void A_successful_launch_frees_no_block()
    {
        // This is the rule that cost the spike the most time. The launcher
        // keeps the blocks of the address and the blocks of the attempt.
        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.True(result.Started);
        Assert.Empty(_allocator.Freed);
    }

    [Fact]
    public void A_successful_launch_gives_the_session_every_block()
    {
        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.NotEmpty(_allocator.Given);
        Assert.Equal(_allocator.Given.Count, Session(result).KeptBlockCount);
    }

    [Fact]
    public void A_successful_launch_keeps_the_blocks_of_the_address_and_of_the_attempt()
    {
        var result = Build().Launch(LobbyLaunchRequest.ToJoin(HostAddress));

        // The address holder takes four blocks: the provider chunk, the text of
        // the address, the array of chunks, and the buffer. The attempt holder
        // takes four more: the name of the player, the two structs, and the
        // DPLCONNECTION struct.
        Assert.Equal(8, _allocator.Given.Count);
        Assert.Equal(8, Session(result).KeptBlockCount);
    }

    [Fact]
    public void A_successful_launch_releases_the_lobby_interface_no_more()
    {
        // The launcher released the version 1 object, and it must release the
        // version 3 interface no more. The game still reads it.
        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.True(result.Started);
        Assert.DoesNotContain(_api.UpgradeLobby, _api.Released);
    }

    [Fact]
    public void A_successful_launch_leaves_COM_as_it_is()
    {
        Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Equal(0, _api.ComStops);
    }

    [Fact]
    public void The_session_of_a_successful_launch_is_open()
    {
        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.True(Session(result).IsOpen);
        Assert.Equal(LobbyLaunchFault.None, result.Fault);
    }

    [Fact]
    public void The_session_releases_the_interface_after_the_game_ends()
    {
        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        _gameRuns = false;
        Session(result).Dispose();

        Assert.Equal([_api.UpgradeLobby], _api.Released.Skip(1));
        Assert.Equal(1, _api.ComStops);
    }

    [Fact]
    public void The_session_releases_nothing_while_the_game_runs()
    {
        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Session(result).Dispose();

        Assert.DoesNotContain(_api.UpgradeLobby, _api.Released);
        Assert.Equal(0, _api.ComStops);
    }

    [Fact]
    public void A_failed_attempt_frees_its_own_blocks_and_keeps_none()
    {
        // Attempt 1 fails and attempt 2 starts the game. The launcher frees the
        // five blocks of attempt 1, and it keeps the blocks of the address and
        // of attempt 2.
        _api.RunResults.Enqueue(DirectPlayHResults.MakeError(330));

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.True(result.Started);
        Assert.Equal(2, _api.Runs.Count);
        Assert.Equal(5, _allocator.Freed.Count);
        Assert.Equal(_allocator.Given.Count - 5, Session(result).KeptBlockCount);
    }

    [Fact]
    public void A_launch_that_DirectPlay_refuses_frees_every_block_one_time()
    {
        RefuseEveryAttempt();

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.False(result.Started);
        Assert.NotEmpty(_allocator.Given);
        Assert.Equal(Sorted(_allocator.Given), Sorted(_allocator.Freed));
    }

    [Fact]
    public void A_launch_that_DirectPlay_refuses_releases_the_lobby_interface()
    {
        RefuseEveryAttempt();

        Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Contains(_api.UpgradeLobby, _api.Released);
        Assert.Equal(1, _api.ComStops);
    }

    [Fact]
    public void A_launch_that_DirectPlay_refuses_reports_DirectPlayRefused()
    {
        RefuseEveryAttempt();

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Equal(LobbyLaunchFault.DirectPlayRefused, result.Fault);
        Assert.Null(result.Session);
    }

    [Fact]
    public void A_launch_that_Windows_will_not_start_reports_GameWillNotStart()
    {
        // DPERR_CANTCREATEPROCESS. A compatibility layer on the game makes
        // Windows demand elevation, and the player can clear that layer.
        var cannotStart = DirectPlayHResults.MakeError(
            DirectPlayHResults.CannotCreateProcessCode);

        for (var i = 0; i < 3; i++) _api.RunResults.Enqueue(cannotStart);

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Equal(LobbyLaunchFault.GameWillNotStart, result.Fault);
    }

    [Fact]
    public void A_launch_with_no_lobby_object_reports_DirectPlayMissing()
    {
        _api.Library = IntPtr.Zero;
        _api.ComCreateResult = DirectPlayHResults.Pointer;

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Equal(LobbyLaunchFault.DirectPlayMissing, result.Fault);
        Assert.Empty(_allocator.Given);
        Assert.Equal(1, _api.ComStops);
    }

    [Fact]
    public void A_join_that_cannot_build_an_address_starts_no_game()
    {
        // A game that starts with no address shows the player its own menus. The
        // launcher must report a failure and not a success.
        _api.AddressSize = 0;

        var result = Build().Launch(LobbyLaunchRequest.ToJoin(HostAddress));

        Assert.Equal(LobbyLaunchFault.DirectPlayRefused, result.Fault);
        Assert.Empty(_api.Runs);
        Assert.Equal(Sorted(_allocator.Given), Sorted(_allocator.Freed));
        Assert.Contains(_api.UpgradeLobby, _api.Released);
    }

    [Fact]
    public void The_launcher_starts_COM_as_apartment_threaded()
    {
        Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Equal([0x2u], _api.ComStarts);
    }

    [Fact]
    public void The_launcher_asks_for_version_3_of_the_lobby_interface()
    {
        Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Equal([DirectPlayIds.IidDirectPlayLobby3A], _api.InterfacesAsked);

        // The version 1 object gave the version 3 interface, so the launcher
        // gives the version 1 object back at once.
        Assert.Equal(_api.ExportLobby, _api.Released[0]);
    }

    [Fact]
    public void The_launcher_falls_back_to_COM_when_the_export_route_fails()
    {
        _api.UpgradeResult = DirectPlayHResults.Pointer;

        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.True(result.Started);
        Assert.Equal(_api.ComLobby, _api.Runs[0].Lobby);
    }

    [Fact]
    public void The_launcher_asks_DirectPlay_for_the_size_before_it_fills_the_buffer()
    {
        Build().Launch(LobbyLaunchRequest.ToJoin(HostAddress));

        Assert.Equal(2, _api.AddressCalls.Count);
        Assert.Equal(IntPtr.Zero, _api.AddressCalls[0].Buffer);
        Assert.NotEqual(IntPtr.Zero, _api.AddressCalls[1].Buffer);

        // Both passes send the same two chunks. A size pass that counted one
        // chunk would ask for too few bytes.
        Assert.Equal(2u, _api.AddressCalls[0].ElementCount);
        Assert.Equal(2u, _api.AddressCalls[1].ElementCount);
    }

    [Fact]
    public void A_join_sends_the_provider_chunk_and_the_address_chunk()
    {
        Build().Launch(LobbyLaunchRequest.ToJoin(HostAddress));

        Assert.Equal(DirectPlayIds.ChunkServiceProvider, _api.Elements[0].DataType);
        Assert.Equal(
            DirectPlayIds.ServiceProviderTcpIp,
            Marshal.PtrToStructure<Guid>(_api.Elements[0].Data));

        Assert.Equal(DirectPlayIds.ChunkInet, _api.Elements[1].DataType);
        Assert.Equal(HostAddress, Marshal.PtrToStringAnsi(_api.Elements[1].Data));

        // The byte count holds the terminating zero byte.
        Assert.Equal((uint)HostAddress.Length + 1, _api.Elements[1].DataSize);
    }

    [Fact]
    public void A_host_sends_the_create_session_flag_and_the_name_of_the_race()
    {
        var result = Build().Launch(
            LobbyLaunchRequest.ToHost(playerName: "Dallin", sessionName: "Friday night"));

        Assert.True(result.Started);

        var run = _api.Runs[0];
        Assert.Equal(LobbyConnectionFlags.CreateSession, run.Connection.Flags);
        Assert.Equal("Friday night", run.SessionName);
        Assert.Equal("Dallin", run.PlayerName);
    }

    [Fact]
    public void A_join_sends_the_join_session_flag_and_no_name_of_a_race()
    {
        // Only the host names the race. A player who joins takes the name of
        // the race that he finds.
        var result = Build().Launch(LobbyLaunchRequest.ToJoin(HostAddress, "Sam"));

        Assert.True(result.Started);

        var run = _api.Runs[0];
        Assert.Equal(LobbyConnectionFlags.JoinSession, run.Connection.Flags);
        Assert.Null(run.SessionName);
        Assert.Equal("Sam", run.PlayerName);
    }

    [Fact]
    public void The_first_attempt_sends_the_provider_GUID_and_the_address_together()
    {
        Build().Launch(LobbyLaunchRequest.ToJoin(HostAddress));

        var run = _api.Runs[0];
        Assert.Equal(DirectPlayIds.ServiceProviderTcpIp, run.Connection.ServiceProviderGuid);
        Assert.Equal(_api.AddressCalls[1].Buffer, run.Connection.Address);
        Assert.Equal(_api.AddressSize, run.Connection.AddressSize);
    }

    [Fact]
    public void The_second_attempt_of_a_host_sends_the_provider_GUID_alone()
    {
        _api.RunResults.Enqueue(DirectPlayHResults.MakeError(330));

        Build().Launch(LobbyLaunchRequest.ToHost());

        var run = _api.Runs[1];
        Assert.Equal(DirectPlayIds.ServiceProviderTcpIp, run.Connection.ServiceProviderGuid);
        Assert.Equal(IntPtr.Zero, run.Connection.Address);
        Assert.Equal(0u, run.Connection.AddressSize);
    }

    [Fact]
    public void The_third_attempt_sends_the_address_alone()
    {
        _api.RunResults.Enqueue(DirectPlayHResults.MakeError(330));

        Build().Launch(LobbyLaunchRequest.ToJoin(HostAddress));

        var run = _api.Runs[1];
        Assert.Equal(Guid.Empty, run.Connection.ServiceProviderGuid);
        Assert.Equal(_api.AddressCalls[1].Buffer, run.Connection.Address);
    }

    [Fact]
    public void Every_attempt_sends_the_application_GUID_of_the_group()
    {
        Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Equal(LobbyApplicationReader.ApplicationGuid, _api.Runs[0].Session.ApplicationGuid);
        Assert.Equal(8u, _api.Runs[0].Session.MaxPlayers);
    }

    [Fact]
    public void A_successful_launch_tells_DirectPlay_which_event_to_signal()
    {
        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.True(result.Started);

        var call = Assert.Single(_api.LobbyEvents);
        Assert.Equal(_api.UpgradeLobby, call.Lobby);
        Assert.Equal(_api.ApplicationId, call.ApplicationId);
        Assert.NotEqual(IntPtr.Zero, call.Handle);
    }

    [Fact]
    public void The_report_of_a_successful_launch_names_the_count_of_kept_blocks()
    {
        var result = Build().Launch(LobbyLaunchRequest.ToHost());

        Assert.Contains($"keeps {Session(result).KeptBlockCount} blocks", result.Detail);
        Assert.Contains($"application id is {_api.ApplicationId}", result.Detail);
    }

    private static ILobbySession Session(LobbyLaunchResult result) =>
        Assert.IsAssignableFrom<ILobbySession>(result.Session);

    private static IEnumerable<long> Sorted(IEnumerable<IntPtr> blocks) =>
        blocks.Select(one => one.ToInt64()).OrderBy(one => one);

    /// <summary>A registration that passes every test of the reader.</summary>
    private static LobbyRegistration Registered() => new(
        new LobbyApplicationEntry(
            LobbyApplicationReader.OwnKeyName,
            LobbyApplicationReader.ApplicationGuid,
            @"C:\Games\Midtown Madness 2",
            @"\midtown2.exe",
            null,
            null),
        LobbyRegistrationFault.None);

    /// <summary>This method makes every attempt fail. A host makes three.</summary>
    private void RefuseEveryAttempt()
    {
        for (var i = 0; i < 3; i++)
        {
            _api.RunResults.Enqueue(DirectPlayHResults.MakeError(330));
        }
    }

    private DirectPlayLobbyLauncher Build() => new(
        _api,
        _allocator,
        _ => true,
        Registered,
        () => _gameRuns,
        isWindows: true,
        is64BitProcess: false);
}
