using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This class asks Windows to let one account read the authentication token of
/// ZeroTier. It gives the read permission to that one account, and it gives no
/// other permission. The app never reads the token, and it never copies the
/// token. The app needs no value from the token file.
/// </summary>
/// <remarks>
/// The class names the account by its ID, which Windows calls a SID. It does
/// not build a name of the form DOMAIN\account. A SID has one form on every
/// computer, and Windows translates an account name. So a name would fail on a
/// computer with another language.
/// </remarks>
public sealed class ZeroTierTokenGrant
{
    /// <summary>The program of Windows that changes a permission.</summary>
    public const string Program = "icacls.exe";

    /// <summary>The name of the file that holds the token.</summary>
    public const string TokenFileName = "authtoken.secret";

    /// <summary>
    /// The read permission of icacls. R is read, and R gives nothing more.
    /// </summary>
    private const string ReadPermission = "(R)";

    private const string VendorFolder = "ZeroTier";

    private const string ProductFolder = "One";

    private readonly IProcessRunner _elevatedRunner;
    private readonly string? _accountId;

    /// <param name="elevatedRunner">
    /// Runs the command with administrator rights. A command with the rights
    /// of the player cannot change the permission of the token file.
    /// </param>
    /// <param name="readAccountId">
    /// Reads the ID of the account that runs the app. The app asks one time,
    /// because that ID cannot change while the app runs.
    /// </param>
    /// <param name="readSharedDataFolder">
    /// Reads the folder of Windows that holds the data of every account.
    /// </param>
    public ZeroTierTokenGrant(
        IProcessRunner elevatedRunner,
        Func<string?> readAccountId,
        Func<string>? readSharedDataFolder = null)
    {
        _elevatedRunner = elevatedRunner;
        _accountId = readAccountId();

        TokenPath = Path.Combine(
            (readSharedDataFolder ?? SharedDataFolder)(),
            VendorFolder,
            ProductFolder,
            TokenFileName);
    }

    /// <summary>The path of the file that holds the token.</summary>
    public string TokenPath { get; }

    /// <summary>
    /// This method builds the arguments of the command. The * prefix tells
    /// icacls that the name is an ID of an account. The /grant switch adds one
    /// entry and keeps every other entry. The /grant:r switch would throw the
    /// other entries away, so this method does not use it.
    /// </summary>
    public static string ArgumentsFor(string tokenPath, string accountId)
        => $"\"{tokenPath}\" /grant *{accountId}:{ReadPermission}";

    /// <summary>
    /// This method runs the command. It returns null when the app cannot read
    /// the ID of the account, and then the app ran nothing. The app has no safe
    /// account to name in that case, and it must not name a group instead.
    /// </summary>
    public async Task<ZeroTierGrantRun?> RunAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_accountId)) return null;

        var arguments = ArgumentsFor(TokenPath, _accountId);
        var result = await _elevatedRunner.RunAsync(Program, arguments, ct);

        return new ZeroTierGrantRun($"icacls {arguments}", result);
    }

    private static string SharedDataFolder()
        => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
}
