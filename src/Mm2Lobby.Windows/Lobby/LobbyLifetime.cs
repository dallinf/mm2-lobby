using Mm2Lobby.Core.Launch;

namespace Mm2Lobby.Windows.Lobby;

/// <summary>
/// This class holds everything that a started game may still read: the
/// DirectPlay interface, and the memory blocks of the DPLCONNECTION struct and
/// its parts.
/// <para>
/// The lifetime rule lives in the types of the two things that it owns. The
/// blocks arrive as KeptBlocks, which has no free method, so the session cannot
/// free them. The interface arrives with a release function, and the session
/// calls it only after the game process ends. Dispose therefore releases
/// nothing while the game runs.
/// </para>
/// <para>
/// Every call that touches Windows arrives as a function, so a test on macOS
/// drives the whole class.
/// </para>
/// </summary>
internal sealed class LobbyLifetime : ILobbySession
{
    private readonly IntPtr _lobby;
    private readonly IReadOnlyList<KeptBlocks> _keptBlocks;
    private readonly Func<IntPtr, uint> _release;
    private readonly Func<bool> _gameIsRunning;
    private readonly Func<TimeSpan, bool> _waitForGame;
    private readonly Action? _closeCom;

    public LobbyLifetime(
        IntPtr lobby,
        IReadOnlyList<KeptBlocks> keptBlocks,
        Func<IntPtr, uint> release,
        Func<bool> gameIsRunning,
        Func<TimeSpan, bool> waitForGame,
        Action? closeCom = null)
    {
        _lobby = lobby;
        _keptBlocks = keptBlocks;
        _release = release;
        _gameIsRunning = gameIsRunning;
        _waitForGame = waitForGame;
        _closeCom = closeCom;
    }

    /// <inheritdoc />
    public bool IsOpen { get; private set; } = true;

    /// <inheritdoc />
    public int KeptBlockCount => _keptBlocks.Sum(one => one.Count);

    /// <inheritdoc />
    public bool WaitForGame(TimeSpan timeout)
    {
        // A call into a released interface reads memory that nothing owns.
        if (!IsOpen) return false;

        return _waitForGame(timeout);
    }

    /// <summary>
    /// This method releases the DirectPlay interface, and it releases nothing
    /// while the game process runs. The caller can call it again later, and the
    /// session then tries the release again.
    /// </summary>
    public void Dispose()
    {
        if (!IsOpen) return;
        if (_gameIsRunning()) return;

        IsOpen = false;
        _release(_lobby);
        _closeCom?.Invoke();
    }
}
