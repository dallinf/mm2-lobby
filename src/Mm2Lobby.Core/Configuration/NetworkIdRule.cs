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
