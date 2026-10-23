using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Mm2Lobby.Core.Processes;

namespace Mm2Lobby.Core.ZeroTier;

/// <summary>
/// This class reads the local ZeroTier state. Every method returns null or an
/// empty result, and no method throws. A zerotier-cli that is absent, or that
/// prints a new format, is an expected condition.
/// </summary>
public sealed class ZeroTierClient : IZeroTierClient
{
    // Every empty answer shares this one instance. So a caller that casts the
    // map to Dictionary must not be able to change it.
    private static readonly IReadOnlyDictionary<string, LinkQuality> NoPeers =
        new ReadOnlyDictionary<string, LinkQuality>(
            new Dictionary<string, LinkQuality>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The plain name of the command. macOS and Linux find this name. Windows
    /// does not, so ZeroTierCliLocator gives this class the path of the
    /// command file instead.
    /// </summary>
    public const string PlainCommand = "zerotier-cli";

    private readonly IProcessRunner _runner;
    private readonly string _executable;

    public ZeroTierClient(IProcessRunner runner, string executable = PlainCommand)
    {
        _runner = runner;
        _executable = executable;
    }

    public async Task<ZeroTierStatus?> GetStatusAsync(CancellationToken ct)
    {
        using var json = await ReadJsonAsync(ZeroTierArguments.Info, JsonValueKind.Object, ct);
        if (json is null) return null;

        var root = json.RootElement;
        var nodeId = StringOrNull(root, "address");
        if (string.IsNullOrWhiteSpace(nodeId)) return null;

        var online = root.TryGetProperty("online", out var flag)
            && flag.ValueKind == JsonValueKind.True;

        return new ZeroTierStatus(online, nodeId.ToLowerInvariant());
    }

    public async Task<ZeroTierNetwork?> GetNetworkAsync(string networkId, CancellationToken ct)
    {
        using var json = await ReadJsonAsync(ZeroTierArguments.ListNetworks, JsonValueKind.Array, ct);
        if (json is null) return null;

        foreach (var entry in json.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            var id = StringOrNull(entry, "nwid");
            if (!string.Equals(id, networkId, StringComparison.OrdinalIgnoreCase)) continue;

            var status = StringOrNull(entry, "status") ?? string.Empty;

            var (address, prefix) = FirstIPv4(entry);
            return new ZeroTierNetwork(networkId.ToLowerInvariant(), status, address, prefix);
        }

        return null;
    }

    public async Task<IReadOnlyDictionary<string, LinkQuality>> GetLinkQualityAsync(
        CancellationToken ct)
    {
        using var json = await ReadJsonAsync(ZeroTierArguments.ListPeers, JsonValueKind.Array, ct);
        if (json is null) return NoPeers;

        var map = new Dictionary<string, LinkQuality>(StringComparer.OrdinalIgnoreCase);
        foreach (var peer in json.RootElement.EnumerateArray())
        {
            if (peer.ValueKind != JsonValueKind.Object) continue;

            var role = StringOrNull(peer, "role");
            if (!string.Equals(role, "LEAF", StringComparison.OrdinalIgnoreCase)) continue;

            var nodeId = StringOrNull(peer, "address");
            if (string.IsNullOrWhiteSpace(nodeId)) continue;

            map[nodeId.ToLowerInvariant()] =
                HasLivePath(peer) ? LinkQuality.Direct : LinkQuality.Relayed;
        }

        return map;
    }

    /// <summary>
    /// This method runs one zerotier-cli command. It then parses the output.
    /// It returns null when the command fails, when the output is not JSON, or
    /// when the root element is not of the expected kind. This method disposes
    /// a document that it does not return. So the caller disposes only a
    /// document that it receives.
    /// </summary>
    private async Task<JsonDocument?> ReadJsonAsync(
        string command, JsonValueKind expectedKind, CancellationToken ct)
    {
        var result = await _runner.RunAsync(
            _executable, ZeroTierArguments.For(command), ct);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput)) return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(result.StandardOutput);
        }
        catch (JsonException)
        {
            return null;
        }

        if (document.RootElement.ValueKind == expectedKind) return document;

        document.Dispose();
        return null;
    }

    private static string? StringOrNull(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty(name, out var value)) return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
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
            var text = entry.ValueKind == JsonValueKind.String ? entry.GetString() : null;
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
            if (path.ValueKind != JsonValueKind.Object) continue;

            var active = path.TryGetProperty("active", out var a)
                && a.ValueKind == JsonValueKind.True;
            var expired = path.TryGetProperty("expired", out var e)
                && e.ValueKind == JsonValueKind.True;

            if (active && !expired) return true;
        }

        return false;
    }
}
