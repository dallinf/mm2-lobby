using Mm2Lobby.Core.Launch;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Launch;

public class LobbyLaunchPreconditionsTests
{
    [Fact]
    public void A_32_bit_process_on_Windows_with_the_DirectPlay_file_can_launch()
    {
        Assert.Equal(
            LobbyLaunchFault.None,
            LobbyLaunchPreconditions.Check(isWindows: true, is64BitProcess: false, directPlayFilePresent: true));
    }

    [Fact]
    public void Another_operating_system_blocks_the_launch()
    {
        Assert.Equal(
            LobbyLaunchFault.NotWindows,
            LobbyLaunchPreconditions.Check(isWindows: false, is64BitProcess: false, directPlayFilePresent: true));
    }

    [Fact]
    public void A_64_bit_process_blocks_the_launch()
    {
        // Windows 11 ships a 32 bit dplayx.dll only. A 64 bit process cannot
        // load it. A run on Windows 11 proved this.
        Assert.Equal(
            LobbyLaunchFault.WrongProcessBitness,
            LobbyLaunchPreconditions.Check(isWindows: true, is64BitProcess: true, directPlayFilePresent: true));
    }

    [Fact]
    public void A_missing_DirectPlay_file_blocks_the_launch()
    {
        Assert.Equal(
            LobbyLaunchFault.DirectPlayMissing,
            LobbyLaunchPreconditions.Check(isWindows: true, is64BitProcess: false, directPlayFilePresent: false));
    }

    [Fact]
    public void The_operating_system_comes_before_every_other_fault()
    {
        // The app runs on macOS during development. A report about the bit
        // width of the process would send the reader the wrong way.
        Assert.Equal(
            LobbyLaunchFault.NotWindows,
            LobbyLaunchPreconditions.Check(isWindows: false, is64BitProcess: true, directPlayFilePresent: false));
    }

    [Fact]
    public void The_bit_width_comes_before_the_missing_file()
    {
        // A 64 bit process looks for the file of a 32 bit process, so it can
        // report a missing file that is in fact there.
        Assert.Equal(
            LobbyLaunchFault.WrongProcessBitness,
            LobbyLaunchPreconditions.Check(isWindows: true, is64BitProcess: true, directPlayFilePresent: false));
    }
}
