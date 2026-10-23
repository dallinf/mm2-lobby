using System.Runtime.Versioning;
using Microsoft.Win32;
using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This check looks for a legacy compatibility mode on Midtown Madness 2. A
/// mode such as WINXPSP2 makes Windows ask for administrator rights, and the
/// lobby launch cannot answer that question. The game runs correctly on
/// Windows 11 with no mode, which the owner proved by playing it.
/// </summary>
/// <remarks>
/// The modes live in HKCU, which belongs to the account of the player. So the
/// fix needs no administrator rights and no elevated runner. This check is the
/// one check of the app that fixes something with no permission prompt.
/// <para>
/// The fix removes only the tokens that name a legacy version of Windows.
/// CompatibilityLayers holds that table and holds the rule. A player can carry
/// other tokens, and the fix keeps every one of them.
/// </para>
/// </remarks>
public sealed class CompatibilityModeCheck : ICheck
{
    /// <summary>
    /// The key of the compatibility layers, under HKEY_CURRENT_USER. Windows
    /// names each value with the full path of a program.
    /// </summary>
    public const string KeyPath =
        @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    private readonly AppConfig _config;
    private readonly Func<string, string?> _readLayers;
    private readonly Action<string, string?> _writeLayers;

    /// <summary>The check that the app builds. It reads the real registry.</summary>
    /// <param name="config">The configuration that the window shares with the lobby.</param>
    public CompatibilityModeCheck(AppConfig config)
        : this(config, ReadFromRegistry, WriteToRegistry)
    {
    }

    /// <param name="config">The configuration that the window shares with the lobby.</param>
    /// <param name="readLayers">
    /// Reads the value of one program under the Layers key. It gives null when
    /// the key holds no value for that program.
    /// </param>
    /// <param name="writeLayers">
    /// Writes the value of one program under the Layers key. A null value
    /// deletes the value.
    /// </param>
    internal CompatibilityModeCheck(
        AppConfig config,
        Func<string, string?> readLayers,
        Action<string, string?> writeLayers)
    {
        _config = config;
        _readLayers = readLayers;
        _writeLayers = writeLayers;
    }

    public string Name => "Compatibility mode";

    /// <summary>
    /// This check gives advice. The app raced before this check existed: the
    /// window shows the address, the player types it into MM2, and the race
    /// happens. A player who cannot change the registry, or who does not want
    /// to, therefore keeps everything that they have today. The fix buys the
    /// one click start, and nothing else.
    /// </summary>
    public bool Blocks => false;

    public Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var path = _config.GamePath;

        if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(NoGamePath());

        var modes = CompatibilityLayers.ModesIn(_readLayers(path));

        if (modes.Count == 0)
        {
            return Task.FromResult(new CheckResult(
                Name,
                CheckStatus.Pass,
                "Midtown Madness 2 runs with no legacy compatibility mode.",
                null));
        }

        var named = CompatibilityLayers.Describe(modes);

        return Task.FromResult(new CheckResult(
            Name,
            CheckStatus.Fail,
            $"Windows runs Midtown Madness 2 in {named}. Windows then asks for "
                + "administrator rights, and the one click start cannot give them.",
            Advice(named)));
    }

    public async Task<CheckResult> FixAsync(CancellationToken ct)
    {
        var path = _config.GamePath;

        if (string.IsNullOrWhiteSpace(path)) return NoGamePath();

        var value = _readLayers(path);
        var modes = CompatibilityLayers.ModesIn(value);

        // The game carries no mode, so the fix writes nothing. A write would
        // change a value that is already correct.
        if (modes.Count == 0) return await RunAsync(ct);

        var named = CompatibilityLayers.Describe(modes);

        try
        {
            _writeLayers(path, CompatibilityLayers.Without(value));
        }
        catch (Exception e)
        {
            return new CheckResult(
                Name,
                CheckStatus.Fail,
                $"Windows refused the change: {e.Message}",
                "Open the properties of midtown2.exe and choose the Compatibility tab. "
                    + "Clear the box that runs the program in compatibility mode.");
        }

        var recheck = await RunAsync(ct);

        if (recheck.Status == CheckStatus.Pass)
        {
            return new CheckResult(
                Name,
                CheckStatus.Pass,
                $"The app removed {named} from Midtown Madness 2.",
                null);
        }

        // The write worked, and Windows still reports the mode. This text must
        // differ from the text of RunAsync, because that text asks the player
        // to do what the player has just done.
        return new CheckResult(
            Name,
            CheckStatus.Fail,
            "Windows accepted the change, and it still reports the compatibility mode.",
            "Choose Check again. If the row still reports a problem, report this to "
                + "the person who runs the group.");
    }

    private CheckResult NoGamePath() => new(
        Name,
        CheckStatus.Fail,
        "The app does not know where Midtown Madness 2 is.",
        "Run the Midtown Madness 2 check first.");

    /// <summary>
    /// The text beside the Fix button. The warning stands first, because the
    /// fix changes how the game runs and the player must know that before
    /// they press the button.
    /// </summary>
    /// <param name="named">The modes, as CompatibilityLayers.Describe writes them.</param>
    private static string Advice(string named)
        => "Warning: the fix changes how Windows runs Midtown Madness 2. "
            + $"The fix removes {named} and keeps every other setting of the game. "
            + "Windows asks for no permission, because this setting belongs to your account. "
            + "This check is advice. The app does not stop the race for it. "
            + "Without the fix you can still race: start the game yourself, "
            + "then type the address of the host. "
            + "With the fix the app starts the game in the race for you.";

    /// <summary>
    /// This method reads the real registry. No test covers it, because a test
    /// cannot read a Windows registry on every computer. It holds the read and
    /// nothing else, and CompatibilityLayers holds every decision.
    /// </summary>
    private static string? ReadFromRegistry(string gamePath)
    {
        if (!OperatingSystem.IsWindows()) return null;

        return ReadLayers(gamePath);
    }

    /// <summary>This method writes the real registry. No test covers it.</summary>
    private static void WriteToRegistry(string gamePath, string? value)
    {
        if (!OperatingSystem.IsWindows()) return;

        WriteLayers(gamePath, value);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadLayers(string gamePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);

        return key?.GetValue(gamePath) as string;
    }

    [SupportedOSPlatform("windows")]
    private static void WriteLayers(string gamePath, string? value)
    {
        if (value is null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(gamePath, throwOnMissingValue: false);
            return;
        }

        using var writable = Registry.CurrentUser.CreateSubKey(KeyPath);
        writable.SetValue(gamePath, value, RegistryValueKind.String);
    }
}
