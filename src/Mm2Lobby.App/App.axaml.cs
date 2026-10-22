using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Mm2Lobby.Windows.Checks;

namespace Mm2Lobby.App;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = BuildTheWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindow BuildTheWindow()
    {
        var window = new MainWindow();

        var environment = LobbyEnvironment.CreateReal(() => AskForTheGame(window));
        var model = new LobbyViewModel(environment, new AvaloniaWindowHost(window));

        window.DataContext = model;
        window.Opened += async (_, _) => await model.StartAsync();
        window.Closed += async (_, _) => await model.DisposeAsync();

        return window;
    }

    /// <summary>
    /// Opens the file picker and waits for the player. The view model calls
    /// this method on a thread from the pool, so this method moves the work to
    /// the user interface thread and waits for it. The user interface thread
    /// stays free, so the wait cannot block it.
    /// </summary>
    private static string? AskForTheGame(TopLevel topLevel) => Dispatcher.UIThread
        .InvokeAsync(() => PickTheGameAsync(topLevel))
        .GetAwaiter()
        .GetResult();

    private static async Task<string?> PickTheGameAsync(TopLevel topLevel)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = $"Find {GameLocator.Executable}",
                AllowMultiple = false,
            });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }
}
