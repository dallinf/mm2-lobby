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

        public Task<CheckResult> RunAsync(CancellationToken ct)
        {
            RunCount++;
            if (_throws is not null) throw _throws;
            return Task.FromResult(new CheckResult(Name, _status, "message", "fix it"));
        }

        public Task<CheckResult> FixAsync(CancellationToken ct) => RunAsync(ct);
    }

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
}
