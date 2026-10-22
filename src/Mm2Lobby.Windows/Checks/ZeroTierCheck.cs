using Mm2Lobby.Core.Configuration;
using Mm2Lobby.Core.Preflight;
using Mm2Lobby.Core.ZeroTier;

namespace Mm2Lobby.Windows.Checks;

/// <summary>
/// This check passes when the ZeroTier service is online and the network
/// status is OK. It reads the network ID from the shared AppConfig on every
/// run, so a change in the settings takes effect at the next run.
/// </summary>
public sealed class ZeroTierCheck : ICheck
{
    private const string DownloadPage = "https://www.zerotier.com/download/";

    private const string CentralPage = "https://my.zerotier.com";

    /// <summary>
    /// The name of the box of the window. The app has no settings screen, so
    /// every piece of advice about the ID names this box.
    /// </summary>
    private const string IdBox = "ZeroTier network ID";

    private readonly IZeroTierClient _zeroTier;
    private readonly AppConfig _config;
    private readonly ZeroTierFaultReader _faults;

    /// <param name="zeroTier">The client that reads ZeroTier.</param>
    /// <param name="config">The one configuration of the app.</param>
    /// <param name="faults">
    /// Reads the reason of a failure. The client answers null for every
    /// failure, so the check needs this reader to name the right fix.
    /// </param>
    public ZeroTierCheck(
        IZeroTierClient zeroTier, AppConfig config, ZeroTierFaultReader faults)
    {
        _zeroTier = zeroTier;
        _config = config;
        _faults = faults;
    }

    public string Name => "ZeroTier";

    public async Task<CheckResult> RunAsync(CancellationToken ct)
    {
        var status = await _zeroTier.GetStatusAsync(ct);
        if (status is null)
        {
            return await DescribeTheFaultAsync(ct);
        }

        // The service answers, but it has no link to the ZeroTier servers.
        if (!status.ServiceOnline)
        {
            return Fail(
                "ZeroTier is running, but it is not connected to the ZeroTier servers.",
                "Check your internet connection. Wait one minute, then run the check again.");
        }

        var networkId = _config.ZeroTierNetworkId;
        if (string.IsNullOrWhiteSpace(networkId))
        {
            return Fail(
                "No ZeroTier network ID is set.",
                $"Type the ID of the group in the box named {IdBox} at the top of this window. "
                    + "Then choose Check again. "
                    + $"ZeroTier Central at {CentralPage} shows the ID of your network.");
        }

        var network = await _zeroTier.GetNetworkAsync(networkId, ct);
        if (network is null)
        {
            return Fail(
                $"This computer has not joined network {networkId}.",
                "Right-click the ZeroTier icon in the system tray, choose Join Network, "
                    + $"and enter {networkId}. Or run: zerotier-cli join {networkId}");
        }

        if (!network.IsJoined)
        {
            return NotJoined(networkId, network);
        }

        return new CheckResult(
            Name, CheckStatus.Pass, $"Connected as {network.Address}.", null);
    }

    public Task<CheckResult> FixAsync(CancellationToken ct) => RunAsync(ct);

    /// <summary>
    /// This method names the reason that ZeroTier gave no status, and the fix
    /// for that one reason. The app ran with the name zerotier-cli alone until
    /// now, and Windows does not find that name. The app then told the player
    /// that the service was stopped, and the player looked for the wrong fix.
    /// </summary>
    private async Task<CheckResult> DescribeTheFaultAsync(CancellationToken ct)
    {
        var fault = await _faults.ReadAsync(ct);

        return fault switch
        {
            ZeroTierFault.CommandNotFound => Fail(
                "The app did not find the ZeroTier command on this computer.",
                NotFoundFix()),

            ZeroTierFault.PermissionDenied => Fail(
                "ZeroTier refused the command. Only an administrator can read the "
                    + "ZeroTier authentication token on this computer.",
                "Close this app. "
                    + "Right-click MM2 Lobby and choose Run as administrator. "
                    + "Then run the check again. "
                    + "If you cannot be an administrator on this computer, "
                    + "ask the owner of the computer for help."),

            _ => Fail(
                "The app reached the ZeroTier command, and ZeroTier gave no answer. "
                    + "The ZeroTier service is stopped, or only an administrator can read "
                    + "the ZeroTier authentication token.",
                "Press the Windows key, type Services, and open Services. "
                    + "In the list, find ZeroTier One. "
                    + "If the Status column of ZeroTier One is blank, the service is stopped. "
                    + "Right-click ZeroTier One and choose Start. "
                    + "If the Status column says Running, close this app and run it as an administrator. "
                    + "Then run the check again."),
        };
    }

    /// <summary>
    /// This method names the folders that the app looked in. A computer that
    /// is not Windows names no such folder, so the text has two forms.
    /// </summary>
    private string NotFoundFix()
    {
        var folders = _faults.Command.SearchedFolders;

        var where = folders.Count == 0
            ? "The app found no folder of programs on this computer. "
            : $"The app looked for {ZeroTierCliLocator.WindowsCommandFile} in these folders: "
                + string.Join("; ", folders) + ". ";

        return where
            + $"The app then tried the plain name {ZeroTierCliLocator.PlainCommand}, "
            + "and Windows did not find it. "
            + $"If ZeroTier is not on this computer, install ZeroTier from {DownloadPage}. "
            + "If ZeroTier is on this computer, install ZeroTier again. "
            + "Accept the folder that the installer offers. "
            + "Then run the check again.";
    }

    private CheckResult NotJoined(string networkId, ZeroTierNetwork network)
    {
        return network.Status switch
        {
            "ACCESS_DENIED" => Fail(
                $"The owner of network {networkId} has not approved this computer "
                    + "(status ACCESS_DENIED).",
                "Ask the network owner to approve this computer in ZeroTier Central."),

            "NOT_FOUND" => Fail(
                $"ZeroTier cannot find network {networkId} (status NOT_FOUND).",
                $"Check the ID in the box named {IdBox} at the top of this window. "
                    + $"An ID has {NetworkIdRule.Length} characters. "
                    + $"ZeroTier Central at {CentralPage} shows the ID of your network."),

            "CLIENT_TOO_OLD" => Fail(
                "This version of ZeroTier is too old for the network (status CLIENT_TOO_OLD).",
                $"Update ZeroTier from {DownloadPage}. Then run the check again."),

            "REQUESTING_CONFIGURATION" => Fail(
                "ZeroTier is still asking the network for its settings "
                    + "(status REQUESTING_CONFIGURATION).",
                "Please wait 30 seconds, then run the check again."),

            _ => Fail(
                $"The network status is {network.Status}, and this computer has no address on it.",
                "Wait one minute, then run the check again. "
                    + "If the problem stays, ask the network owner for help."),
        };
    }

    private CheckResult Fail(string message, string fix)
        => new(Name, CheckStatus.Fail, message, fix);
}
