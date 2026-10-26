using Mm2Lobby.Core.Launch;

namespace Mm2Lobby.Core.Tests.Launch;

public class LobbyLaunchResultTests
{
    private sealed class FakeSession : ILobbySession
    {
        public bool IsOpen => true;

        public int KeptBlockCount => 0;

        public bool WaitForGame(TimeSpan timeout) => false;

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Success_carries_the_session_and_reports_no_fault()
    {
        var session = new FakeSession();

        var result = LobbyLaunchResult.Success(session, "the trail");

        Assert.True(result.Started);
        Assert.Same(session, result.Session);
        Assert.Equal(LobbyLaunchFault.None, result.Fault);
        Assert.Equal("the trail", result.Detail);
    }

    [Fact]
    public void Success_refuses_a_launch_with_no_session()
    {
        // The app must hold the session, because DirectPlay reads the memory of
        // the session seconds after the launch. A success with no session would
        // let the app drop the memory.
        Assert.Throws<ArgumentNullException>(
            () => LobbyLaunchResult.Success(null!, "the trail"));
    }

    [Fact]
    public void Failure_carries_the_fault_and_the_text_and_no_session()
    {
        var result = LobbyLaunchResult.Failure(
            LobbyLaunchFault.DirectPlayMissing, "a message", "an action", "the trail");

        Assert.False(result.Started);
        Assert.Null(result.Session);
        Assert.Equal(LobbyLaunchFault.DirectPlayMissing, result.Fault);
        Assert.Equal("a message", result.Message);
        Assert.Equal("an action", result.PlayerAction);
        Assert.Equal("the trail", result.Detail);
    }

    [Fact]
    public void Failure_refuses_the_fault_None()
    {
        Assert.Throws<ArgumentException>(
            () => LobbyLaunchResult.Failure(LobbyLaunchFault.None, "a message", "an action", "the trail"));
    }
}
