namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// The ZeroTier command that the app runs, and the folders that the app
/// looked in.
/// </summary>
/// <param name="FileName">
/// The path of the command file that the locator found. The locator gives the
/// plain name zerotier-cli when it finds no such file.
/// </param>
/// <param name="SearchedFolders">
/// The folders that the locator looked in, in order. The failure text names
/// these folders, so the player can see where the app looked.
/// </param>
public sealed record ZeroTierCommand(string FileName, IReadOnlyList<string> SearchedFolders);
