namespace Mm2Lobby.Core.Tests.ZeroTier;

public static class ZeroTierFixtures
{
    public const string Info = """
    {"address":"a09acf0233","clock":1761068651000,"online":true,
     "tcpFallbackActive":false,"version":"1.14.0"}
    """;

    public const string InfoOffline = """
    {"address":"a09acf0233","clock":1761068651000,"online":false,
     "tcpFallbackActive":false,"version":"1.14.0"}
    """;

    public const string ListNetworks = """
    [{"assignedAddresses":["10.147.17.42/24"],"broadcastEnabled":true,
      "id":"0123456789abcdef","mtu":2800,"name":"mm2",
      "nwid":"0123456789abcdef","portDeviceName":"feth4823","portError":0,
      "status":"OK","type":"PRIVATE"}]
    """;

    public const string ListNetworksNotJoined = """
    [{"assignedAddresses":[],"broadcastEnabled":true,
      "id":"0123456789abcdef","mtu":2800,"name":"mm2",
      "nwid":"0123456789abcdef","portDeviceName":"feth4823","portError":0,
      "status":"REQUESTING_CONFIGURATION","type":"PRIVATE"}]
    """;

    public const string ListNetworksIPv6First = """
    [{"assignedAddresses":["fd01:2345::1/88","10.147.17.42/24"],
      "broadcastEnabled":true,"id":"0123456789abcdef","nwid":"0123456789abcdef",
      "status":"OK","type":"PRIVATE"}]
    """;

    public const string ListPeers = """
    [{"address":"778cde7190","latency":-1,"paths":[],"role":"PLANET",
      "version":"-1.-1.-1"},
     {"address":"bb11223344","latency":12,
      "paths":[{"active":true,"address":"73.14.5.9/9993","expired":false,
                "lastReceive":1761068650000,"preferred":true}],
      "role":"LEAF","version":"1.14.0"},
     {"address":"cc55667788","latency":-1,"paths":[],"role":"LEAF",
      "version":"1.14.0"},
     {"address":"dd99887766","latency":-1,
      "paths":[{"active":false,"address":"81.2.3.4/9993","expired":true,
                "lastReceive":1761000000000,"preferred":false}],
      "role":"LEAF","version":"1.14.0"}]
    """;

    public const string InfoNumericAddress = """
    {"address":123,"online":true}
    """;

    public const string ListNetworksNullEntry = """
    [null]
    """;

    public const string ListNetworksNumericAddress = """
    [{"nwid":"0123456789abcdef","status":"OK","assignedAddresses":[42]}]
    """;

    public const string ListPeersNonObjects = """
    [null,1,"x"]
    """;

    public const string ListPeersNullPath = """
    [{"address":"ee00112233","latency":5,"paths":[null],"role":"LEAF",
      "version":"1.14.0"}]
    """;
}
