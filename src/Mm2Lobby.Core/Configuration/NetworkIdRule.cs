namespace Mm2Lobby.Core.Configuration;

/// <summary>
/// The rule for a ZeroTier network ID. ZeroTier gives every network an ID of
/// 16 characters, and each character is a hexadecimal digit. ZeroTier Central
/// shows that ID.
/// </summary>
/// <remarks>
/// A player pastes the ID. A paste can carry a space at each end, and some
/// pages show the ID in capitals. So TryRead removes the spaces at the ends,
/// and it accepts both letter cases. ZeroTierClient compares the ID against
/// the output of zerotier-cli without regard to the letter case, but the app
/// also prints the ID in a message and writes it to the file. So TryRead
/// answers one form only: lower case.
/// </remarks>
public static class NetworkIdRule
{
    /// <summary>The number of characters of a ZeroTier network ID.</summary>
    public const int Length = 16;

    /// <summary>The number of characters of a ZeroTier node ID.</summary>
    public const int NodeIdLength = 10;

    /// <summary>
    /// Reads the node ID of the controller of one network. The method answers
    /// the empty string when the value is not a valid network ID, and the
    /// empty answer names no node.
    /// </summary>
    /// <remarks>
    /// ZeroTier builds a network ID from the node ID of the controller of that
    /// network, which is 10 characters, and 6 more characters. So the first 10
    /// characters of a valid network ID are the node ID of the controller.
    ///
    /// The controller of a network that ZeroTier Central holds is a machine of
    /// the ZeroTier service. It joins no race, and it runs no copy of this app.
    /// The lobby therefore leaves it out of the player list.
    ///
    /// The method reads a valid ID only. The app holds no network ID before the
    /// player sets one, and a player can paste a part of an ID. A short value
    /// gives no controller, because the first 10 characters of a short value
    /// can be the node ID of a player.
    /// </remarks>
    public static string ControllerNodeId(string? networkId)
        => TryRead(networkId, out var id) ? id[..NodeIdLength] : string.Empty;

    /// <summary>
    /// Reads one network ID. The method returns true when the value holds a
    /// valid ID, and then id holds that ID in lower case. The method returns
    /// false for every other value, and then id holds the empty string.
    /// </summary>
    public static bool TryRead(string? value, out string id)
    {
        id = string.Empty;
        if (value is null) return false;

        var trimmed = value.Trim();
        if (trimmed.Length != Length) return false;

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiHexDigit(character)) return false;
        }

        id = trimmed.ToLowerInvariant();
        return true;
    }
}
