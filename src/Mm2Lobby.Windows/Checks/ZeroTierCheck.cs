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

    private readonly IZeroTierClient _zeroTier;
    private readonly AppConfig _config;

    public ZeroTierCheck(IZeroTierClient zeroTier, AppConfig config)
    {
        _zeroTier = zeroTier;
        _config = config;
    }

    public string Name => "ZeroTier";

    public async Task<CheckResult> RunAsync(CancellationToken ct)
    {
        // zerotier-cli fails in three cases: ZeroTier is not installed, its
        // service is stopped, or the user cannot read the authentication token
        // of the service. Windows lets only an administrator read that token
        // by default. The app cannot tell the three cases apart.
        var status = await _zeroTier.GetStatusAsync(ct);
        if (status is null)
        {
            return Fail(
                "The app cannot reach ZeroTier. ZeroTier is not installed, its service is stopped, "
                    + "or only an administrator can read the ZeroTier authentication token.",
                $"If ZeroTier is not installed, install it from {DownloadPage}. "
                    + "If it is installed, start the ZeroTier One service in Windows Services. "
                    + "If the service runs, close this app and run it as an administrator. "
                    + "Then run the check again.");
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
                "Enter the network ID of the group in the settings.");
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
                "Check the network ID in the settings. The ID has 16 characters."),

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
