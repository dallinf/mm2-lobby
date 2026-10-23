namespace Mm2Lobby.Core.ZeroTier;

/// <summary>
/// This class builds the arguments of one zerotier-cli command. Every caller
/// uses the For method, so a later change adds a switch in one place.
/// ZeroTier has a -T switch that gives the value of the authentication token,
/// and a -D switch that names the folder of that token. This app sends
/// neither switch now.
/// </summary>
public static class ZeroTierArguments
{
    /// <summary>The command that reads the node ID and the online flag.</summary>
    public const string Info = "info";

    /// <summary>The command that reads the networks of this computer.</summary>
    public const string ListNetworks = "listnetworks";

    /// <summary>The command that reads the known nodes and their paths.</summary>
    public const string ListPeers = "listpeers";

    /// <summary>
    /// This method builds the whole argument text of one command. The -j
    /// switch asks zerotier-cli for JSON.
    /// </summary>
    public static string For(string command) => $"-j {command}";
}
