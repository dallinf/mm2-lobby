namespace Mm2Lobby.Core.Launch;

/// <summary>
/// In phase one, this class starts MM2. The player types the host address.
/// In phase two, a DirectPlay lobby client will give the address to MM2. The player will then type nothing.
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
