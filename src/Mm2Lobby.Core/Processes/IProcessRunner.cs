namespace Mm2Lobby.Core.Processes;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct);
}
