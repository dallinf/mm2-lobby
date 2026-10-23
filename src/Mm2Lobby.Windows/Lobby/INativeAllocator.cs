using System.Runtime.InteropServices;

namespace Mm2Lobby.Windows.Lobby;

/// <summary>
/// An implementation gives out blocks of unmanaged memory. The seam lets a test
/// on macOS prove what the launcher frees and what it keeps.
/// </summary>
internal interface INativeAllocator
{
    /// <summary>This method gives out one block of the asked size.</summary>
    IntPtr Allocate(int bytes);

    /// <summary>This method copies a string into a new block, as ANSI text.</summary>
    IntPtr AnsiString(string text);

    /// <summary>This method gives one block back to the operating system.</summary>
    void Free(IntPtr block);
}

/// <summary>The real allocator. It calls the process heap of Windows.</summary>
internal sealed class ProcessNativeAllocator : INativeAllocator
{
    public IntPtr Allocate(int bytes) => Marshal.AllocHGlobal(bytes);

    public IntPtr AnsiString(string text) => Marshal.StringToHGlobalAnsi(text);

    public void Free(IntPtr block) => Marshal.FreeHGlobal(block);
}
