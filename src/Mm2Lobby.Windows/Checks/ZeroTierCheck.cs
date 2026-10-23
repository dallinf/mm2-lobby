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
    private readonly ZeroTierTokenGrant _grant;

    /// <param name="zeroTier">The client that reads ZeroTier.</param>
    /// <param name="config">The one configuration of the app.</param>
    /// <param name="faults">
    /// Reads the reason of a failure. The client answers null for every
    /// failure, so the check needs this reader to name the right fix.
    /// </param>
    /// <param name="grant">
    /// Asks Windows to let this account read the token of ZeroTier. The Fix
    /// uses it for the one failure that it can repair.
    /// </param>
    public ZeroTierCheck(
        IZeroTierClient zeroTier,
        AppConfig config,
        ZeroTierFaultReader faults,
        ZeroTierTokenGrant grant)
    {
        _zeroTier = zeroTier;
        _config = config;
        _faults = faults;
        _grant = grant;
    }

    public string Name => "ZeroTier";

    public async Task<CheckResult> RunAsync(CancellationToken ct)
        => (await RunWithTheFaultAsync(ct)).Result;

    /// <summary>
    /// This method runs the check, and it also gives the reason of a failure.
    /// FixAsync needs that reason, because it changes a permission for one
    /// reason only.
    /// </summary>
    private async Task<(CheckResult Result, ZeroTierFault? Fault)> RunWithTheFaultAsync(
        CancellationToken ct)
    {
        var status = await _zeroTier.GetStatusAsync(ct);
        if (status is null)
        {
            var fault = await _faults.ReadAsync(ct);
            return (DescribeTheFault(fault), fault);
        }

        return (await ReadTheNetworkAsync(status, ct), null);
    }

    private async Task<CheckResult> ReadTheNetworkAsync(
        ZeroTierStatus status, CancellationToken ct)
    {
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

    /// <summary>
    /// The Fix repairs one failure: ZeroTier cannot read its own token. For
    /// every other failure the Fix runs the check again, because the player
    /// must do the work. The Fix reads the reason again, so it changes a
    /// permission only while that reason holds.
    /// </summary>
    public async Task<CheckResult> FixAsync(CancellationToken ct)
    {
        var first = await RunWithTheFaultAsync(ct);
        if (first.Fault != ZeroTierFault.PermissionDenied) return first.Result;

        return await GrantTheReadPermissionAsync(ct);
    }

    /// <summary>
    /// This method asks Windows to let this account read the token file. It
    /// then runs the real command again. An exit code of 0 proves that icacls
    /// ran, and it does not prove that ZeroTier answers now. The elevated
    /// runner reads no output, so the exit code is all that Windows gives.
    /// </summary>
    private async Task<CheckResult> GrantTheReadPermissionAsync(CancellationToken ct)
    {
        var run = await _grant.RunAsync(ct);

        if (run is null)
        {
            return Fail(
                "The app cannot read the ID of your Windows account, "
                    + "so it changed no permission.",
                "Close this app. Then run MM2 Lobby as an administrator. "
                    + "Then run the check again.");
        }

        if (!run.Result.Succeeded)
        {
            return Fail(
                ElevatedCommand.FailureMessage(
                    "Windows did not give this account the read permission",
                    run.Result.ExitCode),
                ElevatedCommand.ManualFix(run.Command));
        }

        var after = await RunWithTheFaultAsync(ct);
        if (after.Result.Status == CheckStatus.Pass) return after.Result;

        if (after.Fault == ZeroTierFault.PermissionDenied)
        {
            return Fail(
                "Windows changed the permission of the token file, and "
                    + "ZeroTier still refuses the command.",
                "Close this app. Then run MM2 Lobby as an administrator. "
                    + "Then run the check again. "
                    + "If that works, tell the person who runs the group.");
        }

        // The token no longer blocks the app, and another cause took its
        // place. The player reads the text of the new cause.
        return after.Result;
    }

    /// <summary>
    /// This method names the reason that ZeroTier gave no status, and the fix
    /// for that one reason. The app ran with the name zerotier-cli alone until
    /// now, and Windows does not find that name. The app then told the player
    /// that the service was stopped, and the player looked for the wrong fix.
    /// </summary>
    private CheckResult DescribeTheFault(ZeroTierFault fault)
    {
        return fault switch
        {
            ZeroTierFault.CommandNotFound => Fail(
                "The app did not find the ZeroTier command on this computer.",
                NotFoundFix()),

            // This is the one Fix of the app that changes a permission, and it
            // changes that permission on a secret. So the text says what the
            // Fix will change before the player presses it.
            ZeroTierFault.PermissionDenied => Fail(
                "ZeroTier refused the command. Only an administrator can read the "
                    + "ZeroTier authentication token on this computer.",
                "Fix asks Windows to let this account read the token file of ZeroTier. "
                    + "Windows asks you for permission one time. "
                    + "The app gives the read permission to this account only, "
                    + "and it changes nothing else on this computer. "
                    + "The app does not read the token, and it does not copy the token. "
                    + "If you would rather change no permission, close this app. "
                    + "Then run MM2 Lobby as an administrator."),

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
