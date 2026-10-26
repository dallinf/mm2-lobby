using Mm2Lobby.Core.Preflight;

namespace Mm2Lobby.Core.Tests.Preflight;

public class PreflightRunnerTests
{
    private sealed class StubCheck : ICheck
    {
        private readonly CheckStatus _status;
        private readonly Exception? _throws;

        public StubCheck(string name, CheckStatus status, Exception? throws = null)
        {
            Name = name;
            _status = status;
            _throws = throws;
        }

        public string Name { get; }

        public int RunCount { get; private set; }

        /// <summary>A test sets this to false to make the check advisory.</summary>
        public bool Blocks { get; init; } = true;

        public Task<CheckResult> RunAsync(CancellationToken ct)
        {
            RunCount++;
            if (_throws is not null) throw _throws;

            // The stub returns Blocks as true here, which is the default of
            // CheckResult. The runner must stamp the answer of the check over
            // it, or an advisory check would still block the lobby.
            return Task.FromResult(new CheckResult(Name, _status, "message", "fix it"));
        }

        public Task<CheckResult> FixAsync(CancellationToken ct) => RunAsync(ct);
    }

    private static CheckResult Advisory(string name, CheckStatus status)
        => new(name, status, "", null) { Blocks = false };

    [Fact]
    public async Task RunAllAsync_returns_one_result_per_check_in_order()
    {
        var runner = new PreflightRunner(new ICheck[]
        {
            new StubCheck("ZeroTier", CheckStatus.Pass),
            new StubCheck("DirectPlay", CheckStatus.Pass),
        });

        var results = await runner.RunAllAsync(default);

        Assert.Equal(new[] { "ZeroTier", "DirectPlay" },
            results.Select(result => result.Name).ToArray());
    }

    [Fact]
    public async Task RunAllAsync_runs_every_check_even_after_one_fails()
    {
        var second = new StubCheck("DirectPlay", CheckStatus.Pass);
        var runner = new PreflightRunner(new ICheck[]
        {
            new StubCheck("ZeroTier", CheckStatus.Fail),
            second,
        });

        await runner.RunAllAsync(default);

        Assert.Equal(1, second.RunCount);
    }

    [Fact]
    public async Task RunAllAsync_turns_a_thrown_check_into_an_unavailable_result()
    {
        var runner = new PreflightRunner(new ICheck[]
        {
            new StubCheck("Firewall", CheckStatus.Pass, new InvalidOperationException("no netsh")),
        });

        var result = Assert.Single(await runner.RunAllAsync(default));

        Assert.Equal("Firewall", result.Name);
        Assert.Equal(CheckStatus.Unavailable, result.Status);
        Assert.Contains("no netsh", result.Message);
    }

    [Fact]
    public async Task RunAllAsync_keeps_running_after_a_check_throws()
    {
        var runner = new PreflightRunner(new ICheck[]
        {
            new StubCheck("Firewall", CheckStatus.Pass, new InvalidOperationException("no netsh")),
            new StubCheck("DirectPlay", CheckStatus.Pass),
        });

        var results = await runner.RunAllAsync(default);

        Assert.Equal(new[] { CheckStatus.Unavailable, CheckStatus.Pass },
            results.Select(result => result.Status).ToArray());
    }

    [Fact]
    public async Task RunAllAsync_lets_a_cancellation_propagate()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var runner = new PreflightRunner(new ICheck[]
        {
            new StubCheck("ZeroTier", CheckStatus.Pass, new OperationCanceledException()),
        });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAllAsync(cancelled.Token));
    }

    [Fact]
    public async Task RunAllAsync_turns_a_foreign_cancellation_into_an_unavailable_result()
    {
        var runner = new PreflightRunner(new ICheck[]
        {
            new StubCheck("Firewall", CheckStatus.Pass, new OperationCanceledException()),
        });

        var result = Assert.Single(await runner.RunAllAsync(default));

        Assert.Equal(CheckStatus.Unavailable, result.Status);
    }

    [Fact]
    public void AllPassed_is_true_only_when_every_check_passed()
    {
        var passed = new[] { new CheckResult("a", CheckStatus.Pass, "", null) };
        var mixed = new[]
        {
            new CheckResult("a", CheckStatus.Pass, "", null),
            new CheckResult("b", CheckStatus.Fail, "", null),
        };

        Assert.True(PreflightRunner.AllPassed(passed));
        Assert.False(PreflightRunner.AllPassed(mixed));
    }

    [Fact]
    public void AllPassed_is_false_when_a_check_is_unavailable()
    {
        var unavailable = new[]
        {
            new CheckResult("a", CheckStatus.Pass, "", null),
            new CheckResult("b", CheckStatus.Unavailable, "", null),
        };

        Assert.False(PreflightRunner.AllPassed(unavailable));
    }

    [Fact]
    public void AllPassed_is_false_for_an_empty_list()
    {
        Assert.False(PreflightRunner.AllPassed(Array.Empty<CheckResult>()));
    }

    [Fact]
    public void A_result_blocks_the_lobby_unless_the_check_says_otherwise()
    {
        // Every check written before the advisory rule blocks the lobby, and
        // none of them names Blocks. The default must therefore be true.
        Assert.True(new CheckResult("a", CheckStatus.Pass, "", null).Blocks);
    }

    [Fact]
    public void AllPassed_ignores_an_advisory_check_that_fails()
    {
        // The app works today without the two advisory checks. A player who
        // cannot change the registry keeps the race that they have now.
        var results = new[]
        {
            new CheckResult("a", CheckStatus.Pass, "", null),
            Advisory("b", CheckStatus.Fail),
        };

        Assert.True(PreflightRunner.AllPassed(results));
    }

    [Fact]
    public void AllPassed_ignores_an_advisory_check_that_could_not_run()
    {
        var results = new[]
        {
            new CheckResult("a", CheckStatus.Pass, "", null),
            Advisory("b", CheckStatus.Unavailable),
        };

        Assert.True(PreflightRunner.AllPassed(results));
    }

    [Fact]
    public void AllPassed_still_reads_every_check_that_blocks()
    {
        var results = new[]
        {
            new CheckResult("a", CheckStatus.Fail, "", null),
            Advisory("b", CheckStatus.Pass),
        };

        Assert.False(PreflightRunner.AllPassed(results));
    }

    [Fact]
    public void AllPassed_is_false_when_every_result_is_advisory()
    {
        // An empty list gives false, and a list of advice alone says as
        // little. Without this rule, a list that holds no blocking check at
        // all would start the lobby.
        Assert.False(PreflightRunner.AllPassed(
            new[] { Advisory("a", CheckStatus.Pass), Advisory("b", CheckStatus.Pass) }));
    }

    [Fact]
    public async Task RunAllAsync_marks_the_result_of_an_advisory_check()
    {
        // The check owns the answer, and the runner copies it onto every
        // result. So a check writes Blocks one time, and no return path of
        // that check can forget it.
        var runner = new PreflightRunner(new ICheck[]
        {
            new StubCheck("ZeroTier", CheckStatus.Fail),
            new StubCheck("Compatibility mode", CheckStatus.Fail) { Blocks = false },
        });

        var results = await runner.RunAllAsync(default);

        Assert.True(results[0].Blocks);
        Assert.False(results[1].Blocks);
    }

    [Fact]
    public async Task RunAllAsync_marks_an_advisory_check_that_throws()
    {
        // A check that throws gives the runner no result to copy, so the
        // runner must build one with the answer of the check. Without this an
        // advisory check that throws would block the lobby.
        var runner = new PreflightRunner(new ICheck[]
        {
            new StubCheck("Compatibility mode", CheckStatus.Fail, new InvalidOperationException("no registry"))
            {
                Blocks = false,
            },
        });

        var result = Assert.Single(await runner.RunAllAsync(default));

        Assert.Equal(CheckStatus.Unavailable, result.Status);
        Assert.False(result.Blocks);
    }
}
