using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Launch;

/// <summary>
/// The lobby client must stay alive after a successful launch. MM2 asks
/// DirectPlay for its settings about three to four seconds later, and a
/// released interface sends the player back to the menus of the game. These
/// tests drive the session with a fake release and a fake game, so they run on
/// macOS.
/// </summary>
public class LobbyLifetimeTests
{
    private readonly IntPtr _lobby = new(0x5000);
    private readonly List<IntPtr> _released = [];
    private readonly List<TimeSpan> _waits = [];

    private bool _gameRuns = true;
    private bool _gameReachedTheLobby;
    private int _comStops;

    private LobbyLifetime Build(params KeptBlocks[] blocks) => new(
        _lobby,
        blocks,
        release: one =>
        {
            _released.Add(one);
            return 0;
        },
        gameIsRunning: () => _gameRuns,
        waitForGame: timeout =>
        {
            _waits.Add(timeout);
            return _gameReachedTheLobby;
        },
        closeCom: () => _comStops++);

    private static KeptBlocks Blocks(int count)
    {
        var allocator = new FakeNativeAllocator();
        var blocks = new NativeBlocks("the test blocks", allocator);

        for (var i = 0; i < count; i++) blocks.Allocate(16);

        return blocks.Keep();
    }

    [Fact]
    public void A_new_session_is_open()
    {
        var session = Build(Blocks(2));

        Assert.True(session.IsOpen);
    }

    [Fact]
    public void The_session_counts_every_block_that_it_keeps()
    {
        var session = Build(Blocks(2), Blocks(3));

        Assert.Equal(5, session.KeptBlockCount);
    }

    [Fact]
    public void A_session_releases_nothing_while_the_game_runs()
    {
        _gameRuns = true;
        var session = Build(Blocks(2));

        session.Dispose();

        Assert.Empty(_released);
        Assert.Equal(0, _comStops);
        Assert.True(session.IsOpen);
    }

    [Fact]
    public void A_session_releases_the_interface_after_the_game_stopped()
    {
        _gameRuns = false;
        var session = Build(Blocks(2));

        session.Dispose();

        Assert.Equal(_lobby, Assert.Single(_released));
        Assert.Equal(1, _comStops);
        Assert.False(session.IsOpen);
    }

    [Fact]
    public void A_session_releases_the_interface_one_time_only()
    {
        _gameRuns = false;
        var session = Build(Blocks(2));

        session.Dispose();
        session.Dispose();

        Assert.Single(_released);
        Assert.Equal(1, _comStops);
    }

    [Fact]
    public void A_session_that_held_the_interface_can_release_it_later()
    {
        // The window asks again when the player closes the app. The game has
        // stopped by then.
        _gameRuns = true;
        var session = Build(Blocks(2));

        session.Dispose();
        _gameRuns = false;
        session.Dispose();

        Assert.Single(_released);
        Assert.False(session.IsOpen);
    }

    [Fact]
    public void WaitForGame_reports_that_the_game_reached_the_lobby()
    {
        _gameReachedTheLobby = true;
        var session = Build(Blocks(1));

        Assert.True(session.WaitForGame(TimeSpan.FromSeconds(7)));
        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(_waits));
    }

    [Fact]
    public void WaitForGame_reports_false_after_the_timeout()
    {
        _gameReachedTheLobby = false;
        var session = Build(Blocks(1));

        Assert.False(session.WaitForGame(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_closed_session_calls_nothing_in_DirectPlay()
    {
        // A call into a released interface reads memory that nothing owns.
        _gameRuns = false;
        var session = Build(Blocks(1));
        session.Dispose();

        Assert.False(session.WaitForGame(TimeSpan.FromSeconds(1)));
        Assert.Empty(_waits);
    }
}
