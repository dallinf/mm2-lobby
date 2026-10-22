namespace Mm2Lobby.Core.Launch;

/// <summary>
/// Starts a process and does not wait for it. Mm2Lobby.Windows implements it,
/// which is what keeps System.Diagnostics.Process out of Mm2Lobby.Core.
/// </summary>
public interface IProcessLauncher
{
    void Start(GameLaunchPlan plan);
}
