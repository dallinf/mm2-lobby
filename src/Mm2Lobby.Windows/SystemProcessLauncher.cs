using System.Diagnostics;
using Mm2Lobby.Core.Launch;

namespace Mm2Lobby.Windows;

/// <summary>
/// This class starts the game and does not wait for it. The code disposes the
/// Process object. That action frees the handle and does not stop the game.
/// </summary>
public sealed class SystemProcessLauncher : IProcessLauncher
{
    public void Start(GameLaunchPlan plan)
    {
        using var process = Process.Start(new ProcessStartInfo(plan.ExecutablePath)
        {
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = true,
        });
    }
}
