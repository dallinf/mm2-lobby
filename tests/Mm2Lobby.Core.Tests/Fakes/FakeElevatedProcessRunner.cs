using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Core.Tests.Fakes;

/// <summary>
/// This fake stands for SystemProcessRunner with elevate set to true. That
/// runner cannot capture the streams of an elevated process, so the output is
/// always empty here. The runner also throws for an empty file name, because
/// Process.Start does. A check that reads the output of an elevated process
/// therefore fails in a test, as it fails in production.
/// </summary>
public sealed class FakeElevatedProcessRunner : IProcessRunner
{
    private readonly FakeProcessRunner _inner = new();

    public List<string> Calls => _inner.Calls;

    public FakeElevatedProcessRunner Returns(string fileName, string arguments, int exitCode)
    {
        _inner.Returns(fileName, arguments, new ProcessResult(exitCode, "", ""));
        return this;
    }

    public Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new InvalidOperationException("File name has not been provided.");
        }

        return _inner.RunAsync(fileName, arguments, ct);
    }
}
