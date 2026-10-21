using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Core.ZeroTier;

/// <summary>
/// Reads the local ZeroTier state. Every method answers null or an empty
/// result rather than throwing, because a missing or upgraded zerotier-cli is
/// an expected condition.
/// </summary>
public sealed class ZeroTierClient : IZeroTierClient
{
    private static readonly IReadOnlyDictionary<string, LinkQuality> NoPeers =
        new Dictionary<string, LinkQuality>(StringComparer.OrdinalIgnoreCase);

    private readonly IProcessRunner _runner;
    private readonly string _executable;

    public ZeroTierClient(IProcessRunner runner, string executable = "zerotier-cli")
    {
        _runner = runner;
        _executable = executable;
    }

    public async Task<ZeroTierStatus?> GetStatusAsync(CancellationToken ct)
    {
        var json = await ReadJsonAsync("-j info", ct);
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Object)
        {
            json?.Dispose();
            return null;
        }

        using (json)
        {
            var root = json.RootElement;
            var nodeId = root.TryGetProperty("address", out var address)
                ? address.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(nodeId)) return null;

            var online = root.TryGetProperty("online", out var flag)
                && flag.ValueKind == JsonValueKind.True;

            return new ZeroTierStatus(online, nodeId.ToLowerInvariant());
        }
    }

    public async Task<ZeroTierNetwork?> GetNetworkAsync(string networkId, CancellationToken ct)
    {
        var json = await ReadJsonAsync("-j listnetworks", ct);
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Array)
        {
            json?.Dispose();
            return null;
        }

        using (json)
        {
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                var id = entry.TryGetProperty("nwid", out var nwid) ? nwid.GetString() : null;
                if (!string.Equals(id, networkId, StringComparison.OrdinalIgnoreCase)) continue;

                var status = entry.TryGetProperty("status", out var s)
                    ? s.GetString() ?? string.Empty
                    : string.Empty;

                var (address, prefix) = FirstIPv4(entry);
                return new ZeroTierNetwork(networkId.ToLowerInvariant(), status, address, prefix);
            }

            return null;
        }
    }

    public async Task<IReadOnlyDictionary<string, LinkQuality>> GetLinkQualityAsync(
        CancellationToken ct)
    {
        var json = await ReadJsonAsync("-j listpeers", ct);
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Array)
        {
            json?.Dispose();
            return NoPeers;
        }

        using (json)
        {
            var map = new Dictionary<string, LinkQuality>(StringComparer.OrdinalIgnoreCase);
            foreach (var peer in json.RootElement.EnumerateArray())
            {
                var role = peer.TryGetProperty("role", out var r) ? r.GetString() : null;
                if (!string.Equals(role, "LEAF", StringComparison.OrdinalIgnoreCase)) continue;

                var nodeId = peer.TryGetProperty("address", out var a) ? a.GetString() : null;
                if (string.IsNullOrWhiteSpace(nodeId)) continue;

                map[nodeId.ToLowerInvariant()] =
                    HasLivePath(peer) ? LinkQuality.Direct : LinkQuality.Relayed;
            }

            return map;
        }
    }

    private async Task<JsonDocument?> ReadJsonAsync(string arguments, CancellationToken ct)
    {
        var result = await _runner.RunAsync(_executable, arguments, ct);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput)) return null;

        try
        {
            return JsonDocument.Parse(result.StandardOutput);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (IPAddress? Address, int PrefixLength) FirstIPv4(JsonElement network)
    {
        if (!network.TryGetProperty("assignedAddresses", out var assigned)
            || assigned.ValueKind != JsonValueKind.Array)
        {
            return (null, 0);
        }

        foreach (var entry in assigned.EnumerateArray())
        {
            var text = entry.GetString();
            if (text is null) continue;

            var slash = text.IndexOf('/');
            if (slash <= 0) continue;

            if (!IPAddress.TryParse(text[..slash], out var address)) continue;
            if (address.AddressFamily != AddressFamily.InterNetwork) continue;
            if (!int.TryParse(text[(slash + 1)..], out var prefix)) continue;
            if (prefix is < 0 or > 32) continue;

            return (address, prefix);
        }

        return (null, 0);
    }

    private static bool HasLivePath(JsonElement peer)
    {
        if (!peer.TryGetProperty("paths", out var paths)
            || paths.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var path in paths.EnumerateArray())
        {
            var active = path.TryGetProperty("active", out var a)
                && a.ValueKind == JsonValueKind.True;
            var expired = path.TryGetProperty("expired", out var e)
                && e.ValueKind == JsonValueKind.True;

            if (active && !expired) return true;
        }

        return false;
    }
}
