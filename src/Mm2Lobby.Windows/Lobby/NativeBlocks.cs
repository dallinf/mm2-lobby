namespace Mm2Lobby.Windows.Lobby;

/// <summary>
/// Memory blocks that nothing can free. NativeBlocks.Keep builds this object
/// and gives up its own blocks, so no later Dispose reaches them.
/// <para>
/// This type has no free method and no Dispose method, and that absence is the
/// point of the type. MM2 asks DirectPlay for its settings about three to four
/// seconds after the launch, and DirectPlay reads these blocks at that moment.
/// A free before that moment sends the player back to the menus of the game.
/// The spike lost the most time to that one bug.
/// </para>
/// <para>
/// Windows takes the memory back when the process ends. A long lived process
/// therefore leaks one small set of blocks for each race, and that cost buys
/// the only behaviour that works.
/// </para>
/// </summary>
internal sealed class KeptBlocks
{
    private readonly IntPtr[] _blocks;

    internal KeptBlocks(IEnumerable<IntPtr> blocks) => _blocks = blocks.ToArray();

    /// <summary>The count of blocks that this object holds.</summary>
    public int Count => _blocks.Length;
}

/// <summary>
/// This class owns every block that one part of a launch allocated. The owner
/// of the class makes one choice at the end of the launch, and the type makes
/// that choice final.
/// <para>
/// Dispose frees the blocks. Keep hands the blocks to a KeptBlocks, which has
/// no free method, and leaves this holder empty. A Dispose after a Keep
/// therefore frees nothing, and no later call can undo the Keep.
/// </para>
/// </summary>
internal sealed class NativeBlocks : IDisposable
{
    private readonly List<IntPtr> _blocks = [];
    private readonly INativeAllocator _allocator;

    private bool _gaveUp;
    private bool _freed;

    public NativeBlocks(string name, INativeAllocator allocator)
    {
        Name = name;
        _allocator = allocator;
    }

    /// <summary>The name of this holder. A report names it.</summary>
    public string Name { get; }

    /// <summary>The count of blocks that this holder still owns.</summary>
    public int Count => _blocks.Count;

    /// <summary>This method takes one new block of the asked size.</summary>
    public IntPtr Allocate(int bytes)
    {
        Guard();

        var block = _allocator.Allocate(bytes);
        _blocks.Add(block);
        return block;
    }

    /// <summary>This method takes one new block that holds ANSI text.</summary>
    public IntPtr AnsiString(string text)
    {
        Guard();

        var block = _allocator.AnsiString(text);
        _blocks.Add(block);
        return block;
    }

    /// <summary>
    /// This method gives every block to a KeptBlocks and empties this holder.
    /// The caller calls it as soon as DirectPlay reports that it started the
    /// game.
    /// </summary>
    public KeptBlocks Keep()
    {
        Guard();

        var kept = new KeptBlocks(_blocks);
        _blocks.Clear();
        _gaveUp = true;
        return kept;
    }

    /// <summary>
    /// This method frees every block that this holder still owns. It frees
    /// nothing after a Keep, and it frees each block one time only.
    /// </summary>
    public void Dispose()
    {
        _freed = true;

        foreach (var block in _blocks) _allocator.Free(block);

        _blocks.Clear();
    }

    private void Guard()
    {
        ObjectDisposedException.ThrowIf(_freed, this);

        if (_gaveUp)
        {
            throw new InvalidOperationException(
                $"The holder \"{Name}\" gave its blocks to a KeptBlocks, so it takes no more.");
        }
    }
}
