namespace Mm2Lobby.Core.Launch;

/// <summary>
/// Phase one: start MM2 and let the player type the host address. Phase two
/// will add a DirectPlay lobby client that supplies the address instead.
/// </summary>
public sealed class GameLauncher
{
    private readonly IProcessLauncher _launcher;

    public GameLauncher(IProcessLauncher launcher) => _launcher = launcher;

    public static GameLaunchPlan BuildPlan(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            throw new ArgumentException("The game path is empty.", nameof(gamePath));
        }

        var full = Path.GetFullPath(gamePath);
        var folder = Path.GetDirectoryName(full);

        if (string.IsNullOrEmpty(folder))
        {
            throw new ArgumentException("The game path has no folder.", nameof(gamePath));
        }

        return new GameLaunchPlan(full, folder);
    }

    public void Launch(string gamePath) => _launcher.Start(BuildPlan(gamePath));
}
