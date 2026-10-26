using System.Runtime.InteropServices;

namespace Mm2Lobby.LobbyProbe;

/// <summary>
/// This class holds every block of unmanaged memory that the probe allocated.
/// <para>
/// WARNING: do not free this memory after a successful RunApplication. MM2
/// takes several seconds to reach its network code. It then asks DirectPlay
/// for the connection settings, and DirectPlay may read the DPLCONNECTION
/// struct, the DPSESSIONDESC2 struct, the DPNAME struct, the address and the
/// ANSI strings at that moment. A free before that moment can give the game an
/// empty address or a crash.
/// </para>
/// <para>
/// The Keep method turns FreeAll into a report. The probe calls Keep as soon
/// as RunApplication reports success. A failed call frees the memory as
/// before, because a failure starts no game.
/// </para>
/// </summary>
internal sealed class MemoryHolder(string name)
{
    private readonly List<IntPtr> _blocks = [];

    /// <summary>True after the probe decided to keep the memory.</summary>
    public bool Kept { get; private set; }

    /// <summary>This method allocates a block and keeps the address.</summary>
    public IntPtr Alloc(int bytes)
    {
        var block = Marshal.AllocHGlobal(bytes);
        _blocks.Add(block);
        return block;
    }

    /// <summary>This method copies a string to unmanaged memory as ANSI text.</summary>
    public IntPtr AnsiString(string text)
    {
        var block = Marshal.StringToHGlobalAnsi(text);
        _blocks.Add(block);
        return block;
    }

    /// <summary>
    /// This method stops every later free. The caller names the reason, and
    /// the probe prints the reason.
    /// </summary>
    public void Keep(string reason)
    {
        Kept = true;
        Log.Good($"The probe keeps {_blocks.Count} blocks of memory in \"{name}\". {reason}");
    }

    /// <summary>
    /// This method frees every block, or reports that it kept them. It is safe
    /// to call more than one time.
    /// </summary>
    public void FreeAll()
    {
        if (Kept)
        {
            Log.Info(
                $"The probe does not free the {_blocks.Count} blocks in \"{name}\". "
                + "DirectPlay may still read them.");
            return;
        }

        var count = 0;
        foreach (var block in _blocks)
        {
            try
            {
                Marshal.FreeHGlobal(block);
                count++;
            }
            catch (Exception ex)
            {
                Log.Failure($"The free of a block in \"{name}\"", ex);
            }
        }

        _blocks.Clear();
        if (count > 0) Log.Info($"The probe freed {count} blocks of memory in \"{name}\".");
    }
}
