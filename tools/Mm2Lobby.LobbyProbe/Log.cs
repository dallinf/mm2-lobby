namespace Mm2Lobby.LobbyProbe;

/// <summary>
/// This class prints the report of the probe. It also keeps the list of
/// guesses, so the probe can print the whole list again at the end. The owner
/// reads that list first when a step fails.
/// </summary>
internal static class Log
{
    private static readonly List<string> Guesses = [];
    private static int _step;

    /// <summary>Every guess that the probe printed, in order.</summary>
    public static IReadOnlyList<string> AllGuesses => Guesses;

    public static void Heading(string text)
    {
        Console.WriteLine();
        Console.WriteLine("================================================================");
        Console.WriteLine(text);
        Console.WriteLine("================================================================");
    }

    /// <summary>
    /// This method prints the name of a step and raises the step number. The
    /// owner names the step number when he reports a failure.
    /// </summary>
    public static void Step(string text)
    {
        _step++;
        Console.WriteLine();
        Console.WriteLine($"[step {_step}] {text}");
    }

    public static void Info(string text) => Console.WriteLine($"  {text}");

    public static void Item(string name, string value) =>
        Console.WriteLine($"    {name} = {value}");

    /// <summary>
    /// This method prints a guess and keeps it. A guess is a value that the
    /// probe could not read and had to choose. A failed step tells the owner
    /// which guess was wrong.
    /// </summary>
    public static void Guess(string text)
    {
        Guesses.Add(text);
        Console.WriteLine($"  GUESS: {text}");
    }

    public static void Problem(string text) => Console.WriteLine($"  PROBLEM: {text}");

    public static void Good(string text) => Console.WriteLine($"  GOOD: {text}");

    /// <summary>
    /// This method prints the result of a call. It names the call, so a bare
    /// code never appears alone.
    /// </summary>
    public static void Result(string call, int hr) =>
        Console.WriteLine($"  {call} returned {HResults.Describe(hr)}");

    /// <summary>
    /// This method prints an exception. The probe must not stop at an
    /// exception, so every step calls this method and goes on.
    /// </summary>
    public static void Failure(string where, Exception ex)
    {
        Console.WriteLine($"  PROBLEM: {where} threw {ex.GetType().FullName}");
        Console.WriteLine($"  Message: {Shorten(ex.Message)}");

        if (ex is System.Runtime.InteropServices.SEHException seh)
        {
            Console.WriteLine($"  HRESULT of the exception: {HResults.Describe(seh.ErrorCode)}");
        }

        if (ex.InnerException is not null)
        {
            Console.WriteLine($"  Inner exception: {ex.InnerException.GetType().FullName}");
            Console.WriteLine($"  Inner message: {Shorten(ex.InnerException.Message)}");
        }

        // A failed library load on a computer that is not Windows writes a
        // message of thousands of characters. The cut keeps the report short.
        Console.WriteLine("  Stack:");
        foreach (var line in (ex.StackTrace ?? "(none)").Split('\n'))
        {
            Console.WriteLine($"    {line.TrimEnd('\r')}");
        }
    }

    private const int MessageLimit = 1500;

    private static string Shorten(string text) =>
        text.Length <= MessageLimit
            ? text
            : $"{text[..MessageLimit]} ... (the probe cut {text.Length - MessageLimit} characters)";
}
