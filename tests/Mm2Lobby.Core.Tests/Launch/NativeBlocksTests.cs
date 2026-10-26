using Mm2Lobby.Core.Tests.Fakes;
using Mm2Lobby.Windows.Lobby;

namespace Mm2Lobby.Core.Tests.Launch;

/// <summary>
/// The hardest bug of the spike was a free after a successful launch. MM2 asks
/// DirectPlay for its settings about three to four seconds after it starts, and
/// DirectPlay reads these blocks at that moment. A free before that moment
/// sends the player back to the menus of the game.
/// </summary>
public class NativeBlocksTests
{
    private static NativeBlocks Build(FakeNativeAllocator allocator) =>
        new("the test blocks", allocator);

    [Fact]
    public void A_holder_that_kept_its_blocks_frees_nothing()
    {
        var allocator = new FakeNativeAllocator();
        var blocks = Build(allocator);

        blocks.Allocate(16);
        blocks.Allocate(32);
        blocks.AnsiString("10.147.17.42");

        var kept = blocks.Keep();
        blocks.Dispose();

        Assert.Equal(3, kept.Count);
        Assert.Empty(allocator.Freed);
    }

    [Fact]
    public void A_holder_that_kept_its_blocks_frees_nothing_after_many_calls_of_Dispose()
    {
        var allocator = new FakeNativeAllocator();
        var blocks = Build(allocator);

        blocks.Allocate(16);
        blocks.Keep();

        blocks.Dispose();
        blocks.Dispose();

        Assert.Empty(allocator.Freed);
    }

    [Fact]
    public void A_holder_that_did_not_keep_its_blocks_frees_every_block()
    {
        var allocator = new FakeNativeAllocator();
        var blocks = Build(allocator);

        blocks.Allocate(16);
        blocks.Allocate(32);
        blocks.AnsiString("a player");

        blocks.Dispose();

        Assert.Equal(3, allocator.Given.Count);
        Assert.Equal(allocator.Given.Order().ToList(), allocator.Freed.Order().ToList());
    }

    [Fact]
    public void A_second_Dispose_frees_each_block_one_time_only()
    {
        var allocator = new FakeNativeAllocator();
        var blocks = Build(allocator);

        blocks.Allocate(16);

        blocks.Dispose();
        blocks.Dispose();

        Assert.Single(allocator.Freed);
    }

    [Fact]
    public void The_holder_counts_the_blocks_that_it_holds()
    {
        var allocator = new FakeNativeAllocator();
        var blocks = Build(allocator);

        Assert.Equal(0, blocks.Count);

        blocks.Allocate(16);
        blocks.AnsiString("a player");

        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void Keep_takes_the_blocks_away_from_the_holder()
    {
        var allocator = new FakeNativeAllocator();
        var blocks = Build(allocator);

        blocks.Allocate(16);
        blocks.Keep();

        Assert.Equal(0, blocks.Count);
    }

    [Fact]
    public void A_holder_that_kept_its_blocks_takes_no_new_block()
    {
        var allocator = new FakeNativeAllocator();
        var blocks = Build(allocator);

        blocks.Keep();

        Assert.Throws<InvalidOperationException>(() => blocks.Allocate(16));
        Assert.Throws<InvalidOperationException>(() => blocks.AnsiString("a player"));
        Assert.Throws<InvalidOperationException>(() => blocks.Keep());
    }

    [Fact]
    public void A_holder_that_freed_its_blocks_takes_no_new_block()
    {
        // A block from a holder that already freed its blocks would reach
        // DirectPlay as an address that nothing owns.
        var allocator = new FakeNativeAllocator();
        var blocks = Build(allocator);

        blocks.Dispose();

        Assert.Throws<ObjectDisposedException>(() => blocks.Allocate(16));
        Assert.Throws<ObjectDisposedException>(() => blocks.AnsiString("a player"));
        Assert.Throws<ObjectDisposedException>(() => blocks.Keep());
    }

    [Fact]
    public void The_holder_gives_the_text_of_a_string_to_the_allocator()
    {
        var allocator = new FakeNativeAllocator();
        var blocks = Build(allocator);

        blocks.AnsiString("10.147.17.42");

        Assert.Equal("10.147.17.42", Assert.Single(allocator.Strings));
    }
}
