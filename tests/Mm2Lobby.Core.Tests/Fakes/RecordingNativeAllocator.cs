using System.Runtime.InteropServices;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Fakes;

/// <summary>
/// This allocator gives out real memory and records every free.
/// <para>
/// The launcher writes a struct into each block that it takes, so a block must
/// be an address that the process owns. FakeNativeAllocator hands out numbers
/// and suits a test that writes nothing. A test that drives the launcher needs
/// this allocator instead.
/// </para>
/// <para>
/// Free records the block and gives nothing back to the operating system. The
/// test therefore reads a struct after the launcher freed its block, and
/// Dispose gives every block back one time at the end of the test.
/// </para>
/// </summary>
internal sealed class RecordingNativeAllocator : INativeAllocator, IDisposable
{
    private readonly List<IntPtr> _live = [];

    /// <summary>Every block that the allocator gave out, in order.</summary>
    public List<IntPtr> Given { get; } = [];

    /// <summary>Every block that the launcher freed, in order.</summary>
    public List<IntPtr> Freed { get; } = [];

    /// <summary>The text of each string that the launcher asked for, in order.</summary>
    public List<string> Strings { get; } = [];

    public IntPtr Allocate(int bytes)
    {
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));

        return Hold(Marshal.AllocHGlobal(bytes));
    }

    public IntPtr AnsiString(string text)
    {
        Strings.Add(text);

        return Hold(Marshal.StringToHGlobalAnsi(text));
    }

    public void Free(IntPtr block) => Freed.Add(block);

    public void Dispose()
    {
        foreach (var block in _live) Marshal.FreeHGlobal(block);

        _live.Clear();
    }

    private IntPtr Hold(IntPtr block)
    {
        _live.Add(block);
        Given.Add(block);
        return block;
    }
}
