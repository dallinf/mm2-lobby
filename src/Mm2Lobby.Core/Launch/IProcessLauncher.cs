namespace Mm2Lobby.Core.Launch;

/// <summary>
/// An implementation starts a process. It does not wait for the process.
/// Mm2Lobby.Windows implements this interface. Mm2Lobby.Core therefore does not use System.Diagnostics.Process.
/// </summary>
public interface IProcessLauncher
{
    void Start(GameLaunchPlan plan);
}
