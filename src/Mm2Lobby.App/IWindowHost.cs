namespace Mm2Lobby.App;

/// <summary>
/// The user interface services that the view model needs.
/// AvaloniaWindowHost is the real implementation. A test supplies another
/// implementation, so the view model holds no Avalonia type and no test needs
/// a window or a timer.
/// </summary>
public interface IWindowHost
{
    /// <summary>
    /// Runs the work on the user interface thread. LobbyService raises
    /// PeersChanged on the pump thread, and a binding that another thread
    /// touches throws.
    /// </summary>
    void Post(Action work);

    /// <summary>
    /// Runs the work on the user interface thread, once every period. The
    /// caller disposes the result to stop the repeat.
    /// </summary>
    IDisposable Repeat(TimeSpan period, Action work);

    /// <summary>Puts the text on the clipboard. This method can throw.</summary>
    Task CopyTextAsync(string text);
}
