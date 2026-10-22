using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// This class does the IPv4 arithmetic for peer discovery. ZeroTier reports an
/// address and a prefix length. The app uses the subnet to decide which senders
/// it trusts. The app uses the broadcast address to reach every peer.
/// </summary>
public static class IPv4Math
{
    public static IPNetwork Subnet(IPAddress address, int prefixLength)
    {
        var raw = ToUInt32(address, prefixLength);
        var mask = Mask(prefixLength);
        return new IPNetwork(ToAddress(raw & mask), prefixLength);
    }

    public static IPAddress BroadcastAddress(IPAddress address, int prefixLength)
    {
        var raw = ToUInt32(address, prefixLength);
        var mask = Mask(prefixLength);
        return ToAddress((raw & mask) | ~mask);
    }

    private static uint ToUInt32(IPAddress address, int prefixLength)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("The address must be IPv4.", nameof(address));
        }

        if (prefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prefixLength), prefixLength, "The prefix length must be 0 to 32.");
        }

        return BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
    }

    private static uint Mask(int prefixLength)
        => prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);

    private static IPAddress ToAddress(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }
}
