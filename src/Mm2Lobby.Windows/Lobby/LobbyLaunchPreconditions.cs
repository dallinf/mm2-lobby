using Mm2Lobby.Core.Launch;

namespace Mm2Lobby.Windows.Lobby;

/// <summary>
/// This class decides whether this computer and this process can reach
/// DirectPlay at all. The launcher asks before it loads dplayx.dll, so a
/// failure reaches the player as a sentence and not as a load error.
/// </summary>
internal static class LobbyLaunchPreconditions
{
    /// <summary>
    /// This method gives the first fault that blocks the launch, and it gives
    /// LobbyLaunchFault.None when nothing blocks it.
    /// <para>
    /// The order matters. The operating system comes first, because a report
    /// about the bit width of the process would send a reader on macOS the
    /// wrong way. The bit width comes second, because a 64 bit process looks
    /// for the file of a 32 bit process and can report a missing file that is
    /// in fact there.
    /// </para>
    /// </summary>
    public static LobbyLaunchFault Check(
        bool isWindows,
        bool is64BitProcess,
        bool directPlayFilePresent)
    {
        if (!isWindows) return LobbyLaunchFault.NotWindows;

        // Windows 11 ships a 32 bit dplayx.dll only. A run proved that a 64 bit
        // process cannot load it.
        if (is64BitProcess) return LobbyLaunchFault.WrongProcessBitness;

        if (!directPlayFilePresent) return LobbyLaunchFault.DirectPlayMissing;

        return LobbyLaunchFault.None;
    }
}
