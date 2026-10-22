using System.Security.Principal;

namespace Mm2Lobby.Windows;

/// <summary>
/// This class reads the ID of the account that runs the app. Windows calls
/// that ID a SID. The icacls program accepts an ID, and an ID has one form on
/// every computer. An account name differs with the language of Windows, so
/// the app never builds a name.
/// </summary>
public static class WindowsAccount
{
    /// <summary>
    /// This method returns the ID of the account that runs the app. It returns
    /// null on a computer that is not Windows, and when Windows gives no ID.
    /// No test covers this method, because a test cannot read a Windows
    /// account on every computer.
    /// </summary>
    public static string? CurrentId()
    {
        if (!OperatingSystem.IsWindows()) return null;

        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value;
    }
}
