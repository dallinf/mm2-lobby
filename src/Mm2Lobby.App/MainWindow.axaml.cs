using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Mm2Lobby.App;

/// <summary>
/// The window holds the layout and the bindings. Each subscriber below
/// forwards one click to the view model. The window holds no other logic.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// This constructor calls the method that the XAML compiler builds. That
    /// method loads the layout and it also fills the field of every x:Name.
    /// AvaloniaXamlLoader.Load loads the layout alone, and every one of those
    /// fields then stays null: the window threw NullReferenceException on the
    /// way out, and the name of the player never reached the file.
    /// </summary>
    public MainWindow() => InitializeComponent();

    private LobbyViewModel? Model => DataContext as LobbyViewModel;

    private async void OnFixClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model && sender is Control { Tag: string name })
        {
            await model.FixAsync(name);
        }
    }

    private async void OnCheckAgainClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model) await model.CheckAgainAsync();
    }

    private void OnHostClick(object? sender, RoutedEventArgs e) => Model?.Host();

    /// <summary>
    /// Gives the text of the two boxes to the view model. Each binding writes
    /// when the box loses the focus, and a player who types in a box and then
    /// closes the window never gives it that focus.
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (Model is not { } model) return;

        model.DisplayName = NameBox.Text ?? string.Empty;
        model.ZeroTierNetworkId = NetworkIdBox.Text ?? string.Empty;
    }

    private async void OnJoinClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model) await model.JoinAsync();
    }
}
