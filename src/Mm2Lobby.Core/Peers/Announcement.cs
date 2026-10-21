using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mm2Lobby.Core.Peers;

/// <summary>
/// What one app broadcasts about itself every 2 seconds. Keep it small: it
/// travels in a single UDP datagram.
/// </summary>
public sealed record Announcement
{
    public const int CurrentProto = 1;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int Proto { get; init; } = CurrentProto;

    public string AppVersion { get; init; } = string.Empty;

    public string NodeId { get; init; } = string.Empty;

    public string Player { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public bool Hosting { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    public static Announcement? TryParse(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return JsonSerializer.Deserialize<Announcement>(utf8, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
