using Mm2Lobby.Core.Preflight;

namespace Mm2Lobby.Core.Tests.Fakes;

/// <summary>This fake stands for one preflight check.</summary>
public sealed class FakeCheck : ICheck
{
    public FakeCheck(string name, CheckStatus status = CheckStatus.Pass)
    {
        Name = name;
        Status = status;
    }

    public string Name { get; }

    /// <summary>A test sets this to false to make the check advisory.</summary>
    public bool Blocks { get; set; } = true;

    public CheckStatus Status { get; set; }

    public string Message { get; set; } = "The check passes.";

    public string? FixDescription { get; set; }

    public int RunCalls { get; private set; }

    public int FixCalls { get; private set; }

    /// <summary>This method runs inside RunAsync. A test uses it to read the order of the work.</summary>
    public Action? OnRun { get; set; }

    /// <summary>This method runs inside FixAsync. A test uses it to change the result.</summary>
    public Action? OnFix { get; set; }

    /// <summary>The exception that FixAsync throws. A null value means no exception.</summary>
    public Exception? FixFault { get; set; }

    public Task<CheckResult> RunAsync(CancellationToken ct)
    {
        RunCalls++;
        OnRun?.Invoke();
        return Task.FromResult(new CheckResult(Name, Status, Message, FixDescription));
    }

    public Task<CheckResult> FixAsync(CancellationToken ct)
    {
        FixCalls++;

        if (FixFault is not null) throw FixFault;

        OnFix?.Invoke();
        return Task.FromResult(new CheckResult(Name, Status, Message, FixDescription));
    }
}
