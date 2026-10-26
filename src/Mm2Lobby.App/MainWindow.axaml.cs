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

    private async void OnHostClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model) await model.HostAsync();
    }

    /// <summary>
    /// Gives the list of checks the height that the other rows leave. The
    /// setup list therefore scrolls on a short window, and the lobby list and
    /// the two buttons keep their room.
    /// </summary>
    /// <remarks>
    /// A probe measured every row of the window at 880 wide, with four checks
    /// that report a problem and with both messages on screen. Each row
    /// outside the setup panel and the lobby list took 320 together: the two
    /// editors 108, the message of the fix 70, the host panel with the two
    /// buttons 62, and the margins 52. The setup panel takes 113 around this
    /// box: the header, the padding, and the Check again button. The number
    /// below is those two, 433, and 57 for the lobby list.
    ///
    /// The cap therefore leaves the list 57 while it bites, which holds about
    /// two peers. The list is empty in that state, because the lobby does not
    /// start while a check fails. Above 798 the cap passes the 308 that the
    /// rows need, and then no scrollbar appears and the list takes the rest.
    ///
    /// On Windows the text is another size, so these numbers move there. The
    /// formula gives the lobby list away before it clips the buttons, which is
    /// the right order: a player can make the window taller to see more peers,
    /// and a player cannot reach a button below the edge of the screen.
    /// </remarks>
    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
        => SetupScroll.MaxHeight = Math.Max(140, e.NewSize.Height - 490);

    /// <summary>
    /// Gives the text of the two boxes to the view model, which saves the name
    /// and then writes the report of this session. Each binding writes when
    /// the box loses the focus, and a player who types in a box and then
    /// closes the window never gives it that focus.
    /// </summary>
    /// <remarks>
    /// The view model holds the order of that work, and a test proves it. The
    /// report also runs here and not after the close, because the view model
    /// tears the lobby down then: the report would hold an empty list of
    /// players and the title of a window that already stopped.
    /// </remarks>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
        => Model?.OnTheWindowClosing(
            NameBox.Text ?? string.Empty, NetworkIdBox.Text ?? string.Empty);

    private async void OnJoinClick(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model) await model.JoinAsync();
    }

    private void OnStartGameClick(object? sender, RoutedEventArgs e) => Model?.StartGame();
}
