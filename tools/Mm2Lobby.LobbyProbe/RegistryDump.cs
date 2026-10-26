using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Mm2Lobby.LobbyProbe;

/// <summary>One application key under the DirectPlay Applications key.</summary>
internal sealed record LobbyApplication(
    string KeyPath,
    string KeyName,
    IReadOnlyDictionary<string, string> Values);

/// <summary>
/// This class reads the registry and prints every value that it finds. The
/// probe reads the registry and does not call EnumLocalApplications, because
/// EnumLocalApplications needs a COM callback. A callback adds more interop
/// that the author cannot test on this computer, and the registry holds the
/// same data.
/// <para>
/// Every method here carries SupportedOSPlatform. The caller must guard the
/// call with OperatingSystem.IsWindows, or the platform analyzer warns and
/// this project treats a warning as an error.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RegistryDump
{
    private const string Lobby64Path = @"SOFTWARE\Microsoft\DirectPlay\Applications";
    private const string Lobby32Path = @"SOFTWARE\WOW6432Node\Microsoft\DirectPlay\Applications";

    /// <summary>
    /// This method reads the DirectPlay Applications key through three routes
    /// and prints everything that it finds. It returns every application that
    /// it found, with no duplicate key name.
    /// </summary>
    public static List<LobbyApplication> DumpAll()
    {
        var found = new List<LobbyApplication>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Route 1 reads the 32 bit side of the registry by the view. Route 2
        // reads the same keys by the explicit WOW6432Node path. Route 3 reads
        // the 64 bit side. A 32 bit game registers on the 32 bit side, so the
        // owner should expect MM2 on route 1 and route 2.
        ReadOne(RegistryHive.LocalMachine, RegistryView.Registry32, Lobby64Path, found, seen);
        ReadOne(RegistryHive.LocalMachine, RegistryView.Default, Lobby32Path, found, seen);
        ReadOne(RegistryHive.LocalMachine, RegistryView.Registry64, Lobby64Path, found, seen);

        return found;
    }

    private static void ReadOne(
        RegistryHive hive,
        RegistryView view,
        string path,
        List<LobbyApplication> found,
        HashSet<string> seen)
    {
        var label = $"HKLM\\{path} (view {view})";

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path);

            if (key is null)
            {
                Log.Info($"{label}: the key does not exist.");
                return;
            }

            var names = key.GetSubKeyNames();
            Log.Info($"{label}: the key holds {names.Length} application keys.");

            // The Applications key itself may hold values. Print them too.
            PrintValues(key, "    ");

            foreach (var name in names)
            {
                var app = ReadApplication(key, path, name);
                if (app is null) continue;

                if (seen.Add(app.KeyName)) found.Add(app);
            }
        }
        catch (Exception ex)
        {
            Log.Failure($"The read of {label}", ex);
        }
    }

    private static LobbyApplication? ReadApplication(RegistryKey parent, string path, string name)
    {
        Log.Info($"  Application key: \"{name}\"");

        try
        {
            using var key = parent.OpenSubKey(name);
            if (key is null)
            {
                Log.Problem($"  The key \"{name}\" opened as null.");
                return null;
            }

            var values = PrintValues(key, "      ");

            foreach (var child in key.GetSubKeyNames())
            {
                Log.Info($"      Child key: \"{child}\"");
            }

            return new LobbyApplication($"HKLM\\{path}\\{name}", name, values);
        }
        catch (Exception ex)
        {
            Log.Failure($"The read of the key \"{name}\"", ex);
            return null;
        }
    }

    /// <summary>
    /// This method prints every value of one key, verbatim. It keeps the
    /// environment strings as the registry stores them, so the owner sees the
    /// stored text and not an expanded copy.
    /// </summary>
    private static Dictionary<string, string> PrintValues(RegistryKey key, string indent)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string[] names;
        try
        {
            names = key.GetValueNames();
        }
        catch (Exception ex)
        {
            Log.Failure("The read of the value names", ex);
            return result;
        }

        if (names.Length == 0)
        {
            Console.WriteLine($"{indent}(the key holds no values)");
            return result;
        }

        foreach (var name in names)
        {
            var shown = name.Length == 0 ? "(default)" : name;

            string kind;
            try
            {
                kind = key.GetValueKind(name).ToString();
            }
            catch (Exception ex)
            {
                kind = $"unknown, because GetValueKind threw {ex.GetType().Name}";
            }

            try
            {
                var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var text = Render(raw);
                Console.WriteLine($"{indent}{shown} [{kind}] = {text}");

                if (raw is string s) result[name] = s;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{indent}{shown} [{kind}] = the read threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        return result;
    }

    private static string Render(object? raw) => raw switch
    {
        null => "(null)",
        string s => $"\"{s}\"",
        string[] many => $"[{string.Join(", ", many.Select(one => $"\"{one}\""))}]",
        byte[] bytes => $"{bytes.Length} bytes: {Convert.ToHexString(bytes)}",
        int number => $"{number} (0x{number:X8})",
        long number => $"{number} (0x{number:X16})",
        _ => $"{raw} (type {raw.GetType().Name})",
    };
}
