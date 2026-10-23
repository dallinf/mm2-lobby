using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Fakes;

/// <summary>
/// This allocator hands out numbers and records every free. It lets a test on
/// macOS prove the rule that cost the spike the most time: the launcher frees
/// nothing after DirectPlay starts the game.
/// </summary>
internal sealed class FakeNativeAllocator : INativeAllocator
{
    private int _next = 0x1000;

    /// <summary>Every block that the allocator handed out, in order.</summary>
    public List<IntPtr> Given { get; } = [];

    /// <summary>Every block that the caller freed, in order.</summary>
    public List<IntPtr> Freed { get; } = [];

    /// <summary>The text of each string that the caller asked for, in order.</summary>
    public List<string> Strings { get; } = [];

    public IntPtr Allocate(int bytes)
    {
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));

        return Hand();
    }

    public IntPtr AnsiString(string text)
    {
        Strings.Add(text);
        return Hand();
    }

    public void Free(IntPtr block) => Freed.Add(block);

    private IntPtr Hand()
    {
        var block = new IntPtr(_next);
        _next += 0x100;
        Given.Add(block);
        return block;
    }
}
