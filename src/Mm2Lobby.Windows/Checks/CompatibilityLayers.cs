namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This class reads and changes the compatibility layers of one program, as
/// the registry writes them: one line of tokens, for example
/// <c>~ WINXPSP2 RUNASINVOKER</c>.
/// <para>
/// A legacy operating system mode makes Windows ask for administrator rights,
/// and the lobby launch cannot answer that question. So the fix removes those
/// modes. A player can carry other tokens, and the computer of the owner shows
/// DWM8And16BitMitigation on other games. The fix keeps every token that the
/// table below does not name.
/// </para>
/// <para>
/// This class holds the rule alone. The registry lives in
/// CompatibilityModeCheck, so a test on macOS drives every decision here.
/// </para>
/// </summary>
internal static class CompatibilityLayers
{
    /// <summary>
    /// The tokens that name a legacy version of Windows. Only these tokens
    /// make Windows demand elevation, and only these tokens does the fix
    /// remove.
    /// <para>
    /// WARNING: a token that is missing here stays on the game, and the check
    /// then passes over a mode that still breaks the launch. Add a token when
    /// a run finds one.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> OperatingSystemModes =
    [
        "WIN95",
        "WIN98",
        "WINXPSP2",
        "WINXPSP3",
        "VISTARTM",
        "VISTASP1",
        "VISTASP2",
        "WIN7RTM",
        "WIN8RTM",
    ];

    /// <summary>
    /// This method gives every legacy mode of the value, in the order of the
    /// value. It gives the token as the registry writes it, so the player
    /// reads the same word that the properties of the file show.
    /// </summary>
    /// <param name="value">The value of the game under the Layers key, or null.</param>
    public static IReadOnlyList<string> ModesIn(string? value)
        => Tokens(value).Where(IsOperatingSystemMode).ToList();

    /// <summary>
    /// This method gives the value with every legacy mode taken out. It gives
    /// null when nothing is left to keep, and the check then deletes the whole
    /// value.
    /// </summary>
    /// <param name="value">The value of the game under the Layers key, or null.</param>
    public static string? Without(string? value)
    {
        var kept = Tokens(value).Where(one => !IsOperatingSystemMode(one)).ToList();

        // The tilde marks the entry and names no setting. A value that holds
        // the tilde alone therefore means nothing, so the check deletes it.
        if (kept.Count == 0 || kept.All(one => one == "~")) return null;

        return string.Join(' ', kept);
    }

    /// <summary>
    /// This method names the modes for the player: one mode, or two modes
    /// joined by "and", or a list that ends with "and".
    /// </summary>
    /// <param name="modes">The modes that ModesIn found.</param>
    public static string Describe(IReadOnlyList<string> modes)
    {
        var word = modes.Count == 1 ? "compatibility mode" : "compatibility modes";

        var names = modes.Count <= 1
            ? string.Join("", modes)
            : $"{string.Join(", ", modes.Take(modes.Count - 1))} and {modes[^1]}";

        return $"the {names} {word}";
    }

    private static bool IsOperatingSystemMode(string token)
        => OperatingSystemModes.Contains(token, StringComparer.OrdinalIgnoreCase);

    private static string[] Tokens(string? value)
        => value?.Split(' ', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries) ?? [];
}
