using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace Mm2Lobby.App;

/// <summary>
/// The Avalonia implementation of IWindowHost. The view model reaches Avalonia
/// through this class only.
/// </summary>
public sealed class AvaloniaWindowHost : IWindowHost
{
    private readonly TopLevel _topLevel;

    public AvaloniaWindowHost(TopLevel topLevel) => _topLevel = topLevel;

    public void Post(Action work) => Dispatcher.UIThread.Post(work);

    public IDisposable Repeat(TimeSpan period, Action work) => DispatcherTimer.Run(
        () =>
        {
            work();
            return true;
        },
        period);

    public async Task CopyTextAsync(string text)
    {
        var clipboard = _topLevel.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(text);
    }
}
