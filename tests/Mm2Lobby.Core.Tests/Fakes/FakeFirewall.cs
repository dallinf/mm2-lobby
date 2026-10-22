using System.Text.RegularExpressions;
using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Core.Tests.Fakes;

/// <summary>
/// This fake models the rule table of the Windows Firewall and the two runners
/// that reach it. It refuses what the real runners refuse:
/// the plain runner cannot add or delete a rule (netsh needs administrator
/// rights and returns exit code 1); the elevated runner returns no output, so
/// an elevated show gives the check nothing to read but the exit code; netsh returns exit code 1
/// to delete a rule that does not exist; netsh adds a second rule of the same
/// name and does not replace the first; and Process.Start throws for an empty
/// file name. A rule keeps the program path that its add command gave.
/// </summary>
public sealed class FakeFirewall
{
    private static readonly Regex Command = new(
        "^advfirewall firewall (?<verb>show|delete|add) rule name=\"(?<name>[^\"]+)\""
        + "(?: dir=(?<dir>in|out) action=allow program=\"(?<program>[^\"]+)\" enable=yes profile=any)?$",
        RegexOptions.Compiled);

    public sealed record Rule(string Name, string Direction, string Program);

    public FakeFirewall()
    {
        Plain = new Runner(this, elevated: false);
        Elevated = new Runner(this, elevated: true);
    }

    public IProcessRunner Plain { get; }

    public IProcessRunner Elevated { get; }

    public List<Rule> Rules { get; } = new();

    /// <summary>Every call in order, each with the prefix "plain: " or "elevated: ".</summary>
    public List<string> Log { get; } = new();

    public IEnumerable<string> PlainCalls => Calls("plain: ");

    public IEnumerable<string> ElevatedCalls => Calls("elevated: ");

    /// <summary>The exit code of an add for a rule name. The add changes nothing.</summary>
    public Dictionary<string, int> AddFails { get; } = new();

    /// <summary>The exit code of a delete for a rule name. The delete changes nothing.</summary>
    public Dictionary<string, int> DeleteFails { get; } = new();

    /// <summary>A netsh that reports success for an add and creates no rule.</summary>
    public bool AddsAreIgnored { get; set; }

    private IEnumerable<string> Calls(string prefix)
        => Log.Where(call => call.StartsWith(prefix, StringComparison.Ordinal))
            .Select(call => call[prefix.Length..]);

    private sealed class Runner : IProcessRunner
    {
        private readonly FakeFirewall _firewall;
        private readonly bool _elevated;

        public Runner(FakeFirewall firewall, bool elevated)
        {
            _firewall = firewall;
            _elevated = elevated;
        }

        public Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                throw new InvalidOperationException("File name has not been provided.");
            }

            _firewall.Log.Add($"{(_elevated ? "elevated" : "plain")}: {fileName} {arguments}");

            if (fileName != "netsh.exe")
            {
                return Task.FromResult(new ProcessResult(127, "", "command not found"));
            }

            var match = Command.Match(arguments);
            if (!match.Success)
            {
                return Task.FromResult(Result(1, "The syntax supplied for this command is not valid.\r\n"));
            }

            var name = match.Groups["name"].Value;

            return Task.FromResult(match.Groups["verb"].Value switch
            {
                "show" => Show(name),
                "delete" => Delete(name),
                _ => Add(name, match.Groups["dir"].Value, match.Groups["program"].Value),
            });
        }

        // The elevated runner cannot capture output, so its result has none.
        private ProcessResult Result(int exitCode, string output)
            => _elevated ? new ProcessResult(exitCode, "", "") : new ProcessResult(exitCode, output, "");

        private ProcessResult Show(string name)
        {
            var found = _firewall.Rules.Where(rule => rule.Name == name).ToList();
            if (found.Count == 0) return Result(1, "No rules match the specified criteria.\r\n");

            return Result(0, string.Concat(found.Select(rule =>
                $"Rule Name:   {rule.Name}\r\nDirection:   {rule.Direction}\r\nProgram:   {rule.Program}\r\n\r\n")));
        }

        private ProcessResult Delete(string name)
        {
            if (!_elevated) return Result(1, "The requested operation requires elevation.\r\n");
            if (_firewall.DeleteFails.TryGetValue(name, out var code)) return Result(code, "");

            return _firewall.Rules.RemoveAll(rule => rule.Name == name) > 0
                ? Result(0, "Deleted rule(s).\r\n")
                : Result(1, "No rules match the specified criteria.\r\n");
        }

        private ProcessResult Add(string name, string direction, string program)
        {
            if (!_elevated) return Result(1, "The requested operation requires elevation.\r\n");
            if (_firewall.AddFails.TryGetValue(name, out var code)) return Result(code, "");

            if (!_firewall.AddsAreIgnored) _firewall.Rules.Add(new Rule(name, direction, program));
            return Result(0, "Ok.\r\n");
        }
    }
}
