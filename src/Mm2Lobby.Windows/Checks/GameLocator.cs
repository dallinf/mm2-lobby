using Microsoft.Win32;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This class finds midtown2.exe. It reads every value under the registry key
/// of the game and does not name one value, because the value name differs
/// between releases.
/// </summary>
public static class GameLocator
{
    public const string Executable = "midtown2.exe";

    private const string RegistryKey =
        @"SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Midtown Madness 2";

    /// <summary>
    /// This method is true when the path names an existing file called
    /// midtown2.exe. The letter case of the name does not matter.
    /// </summary>
    public static bool IsTheGame(string? path, Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!Path.GetFileName(path).Equals(Executable, StringComparison.OrdinalIgnoreCase)) return false;

        return (fileExists ?? File.Exists)(path);
    }

    /// <summary>
    /// This method returns the first value that names the game file. It also
    /// tries each value as a folder that holds the game file. It skips a blank
    /// value, because Path.Combine would turn a blank value into a relative
    /// path in the current folder.
    /// </summary>
    public static string? FindIn(IEnumerable<string> values, Func<string, bool>? fileExists = null)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            if (IsTheGame(value, fileExists)) return value;

            var candidate = Path.Combine(value, Executable);
            if (IsTheGame(candidate, fileExists)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// This method reads the registry. It returns null on a computer that has
    /// no registry, and when the key is missing. No test covers this method,
    /// because a test cannot read a Windows registry on every computer.
    /// </summary>
    public static string? FindInRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;

        using var key = Registry.LocalMachine.OpenSubKey(RegistryKey);
        if (key is null) return null;

        // The code uses a loop and not a lambda. The platform analyzer does
        // not follow the OperatingSystem guard into a lambda.
        var values = new List<string>();
        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string value) values.Add(value);
        }

        return FindIn(values);
    }
}
