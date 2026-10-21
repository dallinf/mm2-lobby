using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Core.Tests.Fakes;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Dictionary<string, ProcessResult> _responses = new();

    public List<string> Calls { get; } = new();

    public FakeProcessRunner Returns(string fileName, string arguments, ProcessResult result)
    {
        _responses[$"{fileName} {arguments}"] = result;
        return this;
    }

    public FakeProcessRunner ReturnsJson(string fileName, string arguments, string json)
        => Returns(fileName, arguments, new ProcessResult(0, json, ""));

    public Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        var key = $"{fileName} {arguments}";
        Calls.Add(key);
        return Task.FromResult(_responses.TryGetValue(key, out var response)
            ? response
            : new ProcessResult(127, "", "command not found"));
    }
}
