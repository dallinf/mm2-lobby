using Mm2Lobby.App;

namespace Mm2Lobby.Core.Tests.Fakes;

/// <summary>
/// This fake stands for the window. It runs the posted work at once, so a
/// test needs no user interface thread. It also keeps the repeated work, so a
/// test runs one tick without a timer.
/// </summary>
public sealed class FakeWindowHost : IWindowHost
{
    private Action? _repeated;

    /// <summary>The text of every call to CopyTextAsync.</summary>
    public List<string> CopiedText { get; } = new();

    /// <summary>The number of calls to Post.</summary>
    public int PostCount { get; private set; }

    /// <summary>
    /// When this property is false, Post keeps the work instead of running it.
    /// A test then proves that the view model does not touch a binding on the
    /// thread that raised the event.
    /// </summary>
    public bool RunPostedWork { get; set; } = true;

    /// <summary>The work that Post kept.</summary>
    public List<Action> PostedWork { get; } = new();

    public TimeSpan? RepeatPeriod { get; private set; }

    public int RepeatCount { get; private set; }

    public int RepeatStopCount { get; private set; }

    /// <summary>The exception that CopyTextAsync throws. A null value means no exception.</summary>
    public Exception? CopyFault { get; set; }

    public void Post(Action work)
    {
        PostCount++;

        if (RunPostedWork)
        {
            work();
            return;
        }

        PostedWork.Add(work);
    }

    public IDisposable Repeat(TimeSpan period, Action work)
    {
        RepeatPeriod = period;
        RepeatCount++;
        _repeated = work;
        return new Stop(this);
    }

    public Task CopyTextAsync(string text)
    {
        if (CopyFault is not null) return Task.FromException(CopyFault);

        CopiedText.Add(text);
        return Task.CompletedTask;
    }

    /// <summary>Runs the repeated work one time, as a timer tick does.</summary>
    public void Tick() => _repeated?.Invoke();

    /// <summary>Runs the work that Post kept, then forgets it.</summary>
    public void RunPost()
    {
        var work = PostedWork.ToList();
        PostedWork.Clear();

        foreach (var item in work) item();
    }

    private sealed class Stop : IDisposable
    {
        private readonly FakeWindowHost _host;

        public Stop(FakeWindowHost host) => _host = host;

        public void Dispose()
        {
            _host.RepeatStopCount++;
            _host._repeated = null;
        }
    }
}
