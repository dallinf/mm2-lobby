using Mm2Lobby.Core.Launch;

namespace Mm2Lobby.Core.Tests.Fakes;

/// <summary>
/// This fake stands for the DirectPlay lobby launcher. A test says what the
/// next launch answers, and it then reads every request that the window sent.
/// </summary>
public sealed class FakeLobbyLauncher : ILobbyLauncher
{
    /// <summary>Every request that the window sent, in order.</summary>
    public List<LobbyLaunchRequest> Requests { get; } = new();

    /// <summary>Every session that a launch which worked gave back, in order.</summary>
    public List<FakeLobbySession> Sessions { get; } = new();

    /// <summary>
    /// False by default. Most tests drive the ordinary start, which is what
    /// the app did before the lobby path existed. A test of the lobby path
    /// writes true.
    /// </summary>
    public bool CanLaunch { get; set; }

    /// <summary>
    /// The answer of the next launch. A null value means that the launch
    /// starts the game and gives a new session back.
    /// </summary>
    public LobbyLaunchResult? Answer { get; set; }

    /// <summary>The exception that Launch throws. A null value means no exception.</summary>
    public Exception? Fault { get; set; }

    /// <summary>The detail of a launch that works.</summary>
    public string Detail { get; set; } = "the trail of the launch";

    /// <summary>Makes the next launch fail with the fault.</summary>
    public void Fails(LobbyLaunchFault fault, string detail = "the trail of the failure")
        => Answer = LobbyLaunchResult.Failure(fault, detail);

    public LobbyLaunchResult Launch(LobbyLaunchRequest request)
    {
        Requests.Add(request);

        if (Fault is not null) throw Fault;
        if (Answer is not null) return Answer;

        var session = new FakeLobbySession();
        Sessions.Add(session);

        return LobbyLaunchResult.Success(session, Detail);
    }
}

/// <summary>
/// This fake stands for one live session. LobbyLifetime refuses the release
/// while the game process runs, and GameRuns reproduces that refusal.
/// </summary>
public sealed class FakeLobbySession : ILobbySession
{
    public bool IsOpen { get; private set; } = true;

    /// <summary>The number of calls to Dispose.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>
    /// True while the game of this session runs. The session then releases
    /// nothing, as LobbyLifetime does.
    /// </summary>
    public bool GameRuns { get; set; }

    public int KeptBlockCount => 5;

    public bool WaitForGame(TimeSpan timeout) => IsOpen;

    public void Dispose()
    {
        DisposeCount++;

        if (!IsOpen || GameRuns) return;

        IsOpen = false;
    }
}
