using System.Text;
using Mm2Lobby.Core.Configuration;

namespace Mm2Lobby.Core.Diagnostics;

/// <summary>
/// This class writes the report of one session to one file. Each write
/// overwrites the file of the session before it, so the folder of the app
/// holds one report and it does not grow. The last session is the one that
/// answers a question about a failed race.
/// </summary>
public sealed class ReportStore
{
    /// <summary>The name of the file, beside the configuration file.</summary>
    public const string FileName = "report.txt";

    /// <summary>
    /// The encoding of the file. The mark at the start of the file tells an
    /// editor of Windows that the text is UTF-8. The report carries the dash
    /// that the lobby list shows for a peer with no address, and an older
    /// Notepad reads that one character as two characters while the file
    /// carries no mark.
    /// </summary>
    private static readonly Encoding TextEncoding =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <param name="filePath">The path of the one report file.</param>
    public ReportStore(string filePath) => FilePath = filePath;

    /// <summary>The path of the file. The window shows this path to the player.</summary>
    public string FilePath { get; }

    /// <summary>
    /// The path beside the configuration file. ConfigStore.DefaultPath names
    /// the folder of the app, so one change moves both files together.
    /// </summary>
    public static string DefaultPath => Path.Combine(
        Path.GetDirectoryName(ConfigStore.DefaultPath) ?? string.Empty, FileName);

    /// <summary>
    /// This method writes the text, and it overwrites the file of the last
    /// session. It throws when the write fails, and the caller catches: the
    /// app closes in every case.
    /// </summary>
    public void Write(string text)
    {
        var folder = Path.GetDirectoryName(FilePath);

        // The player can close the app before any component saved a setting,
        // and then no component built this folder.
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        // The app writes no temporary file here. ConfigStore moves a whole
        // file onto its target, because a half configuration costs the player
        // their settings. This file holds a report, and a report that a crash
        // cut in half still answers more than no report.
        File.WriteAllText(FilePath, text, TextEncoding);
    }
}
