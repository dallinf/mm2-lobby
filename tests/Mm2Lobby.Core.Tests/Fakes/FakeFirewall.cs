using System.Text.RegularExpressions;
using Mm2Lobby.Core.Processes;
using Mm2Lobby.Windows;

namespace Mm2Lobby.Core.Tests.Fakes;

/// <summary>
/// This fake holds a table of firewall rules and two runners that reach it.
/// The plain runner cannot add, change or delete a rule, because netsh needs
/// administrator rights for that. The elevated runner returns the exit code
/// but no output. Netsh answers exit code 1 for a missing required parameter,
/// and for a set or delete command that matches no rule. A second add command
/// with one name makes a second rule. Process.Start throws for an empty file
/// name.
/// </summary>
/// <remarks>
/// The fake models the forms that the app sends, with the parameters that
/// netsh documents for them. A form that the fake does not model answers exit
/// code 1. The set command uses the name and the optional dir to select
/// rules. The words after new give the new values. The fake assumes that the
/// set command changes a rule in place, so the rule is never missing. That is
/// an assumption. The Windows script must confirm it.
/// </remarks>
public sealed class FakeFirewall
{
    private const string Quoted = "\"(?<{0}>[^\"]+)\"";

    private static readonly Regex Show = Build(
        $"show rule name={string.Format(Quoted, "name")}( dir=(?<dir>in|out))?");

    private static readonly Regex Delete = Build(
        $"delete rule name={string.Format(Quoted, "name")}( dir=(?<dir>in|out))?");

    // Netsh requires dir and action. The program is optional. A rule with no
    // program applies to every program.
    private static readonly Regex Add = Build(
        $"add rule name={string.Format(Quoted, "name")} dir=(?<dir>in|out) action=allow"
        + $"( program={string.Format(Quoted, "program")})?( enable=yes)?( profile=any)?");

    private static readonly Regex Set = Build(
        $"set rule name={string.Format(Quoted, "name")}( dir=(?<dir>in|out))? "
        + $"new program={string.Format(Quoted, "program")}( action=allow)?( enable=yes)?( profile=any)?");

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

    /// <summary>The exit code of an add command for a rule name. The rule table stays the same.</summary>
    public Dictionary<string, int> AddFails { get; } = new();

    /// <summary>The exit code of a set command for a rule name. The rule table stays the same.</summary>
    public Dictionary<string, int> SetFails { get; } = new();

    /// <summary>A netsh that reports success for an add command and creates no rule.</summary>
    public bool AddsAreIgnored { get; set; }

    private static Regex Build(string pattern)
        => new($"^advfirewall firewall {pattern}$", RegexOptions.Compiled);

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
                return Task.FromResult(new ProcessResult(
                    SystemProcessRunner.NotRunExitCode, "", "command not found"));
            }

            return Task.FromResult(Dispatch(arguments));
        }

        private ProcessResult Dispatch(string arguments)
        {
            if (Show.Match(arguments) is { Success: true } show) return DoShow(show);
            if (Delete.Match(arguments) is { Success: true } delete) return DoDelete(delete);
            if (Add.Match(arguments) is { Success: true } add) return DoAdd(add);
            if (Set.Match(arguments) is { Success: true } set) return DoSet(set);

            return Result(1, "The syntax supplied for this command is not valid.\r\n");
        }

        // The elevated runner cannot capture output, so its result has none.
        private ProcessResult Result(int exitCode, string output)
            => _elevated ? new ProcessResult(exitCode, "", "") : new ProcessResult(exitCode, output, "");

        private ProcessResult DoShow(Match match)
        {
            var name = match.Groups["name"].Value;
            var direction = match.Groups["dir"].Value;
            var found = _firewall.Rules
                .Where(rule => rule.Name == name
                    && (direction.Length == 0 || rule.Direction == direction))
                .ToList();
            if (found.Count == 0) return Result(1, "No rules match the specified criteria.\r\n");

            return Result(0, string.Concat(found.Select(rule =>
                $"Rule Name:   {rule.Name}\r\nDirection:   {rule.Direction}\r\nProgram:   {rule.Program}\r\n\r\n")));
        }

        private ProcessResult DoDelete(Match match)
        {
            if (!_elevated) return Result(1, "The requested operation requires elevation.\r\n");

            var name = match.Groups["name"].Value;
            var direction = match.Groups["dir"].Value;

            var removed = _firewall.Rules.RemoveAll(rule => rule.Name == name
                && (direction.Length == 0 || rule.Direction == direction));

            return removed > 0
                ? Result(0, "Deleted rule(s).\r\n")
                : Result(1, "No rules match the specified criteria.\r\n");
        }

        private ProcessResult DoAdd(Match match)
        {
            if (!_elevated) return Result(1, "The requested operation requires elevation.\r\n");

            var name = match.Groups["name"].Value;
            if (_firewall.AddFails.TryGetValue(name, out var code)) return Result(code, "");

            if (!_firewall.AddsAreIgnored)
            {
                var program = match.Groups["program"].Success ? match.Groups["program"].Value : "Any";
                _firewall.Rules.Add(new Rule(name, match.Groups["dir"].Value, program));
            }

            return Result(0, "Ok.\r\n");
        }

        private ProcessResult DoSet(Match match)
        {
            if (!_elevated) return Result(1, "The requested operation requires elevation.\r\n");

            var name = match.Groups["name"].Value;
            if (_firewall.SetFails.TryGetValue(name, out var code)) return Result(code, "");

            var direction = match.Groups["dir"].Value;
            var matches = _firewall.Rules
                .Select((rule, index) => (rule, index))
                .Where(entry => entry.rule.Name == name
                    && (direction.Length == 0 || entry.rule.Direction == direction))
                .ToList();

            if (matches.Count == 0) return Result(1, "No rules match the specified criteria.\r\n");

            foreach (var (rule, index) in matches)
            {
                _firewall.Rules[index] = rule with { Program = match.Groups["program"].Value };
            }

            return Result(0, "Updated rule(s).\r\n");
        }
    }
}
