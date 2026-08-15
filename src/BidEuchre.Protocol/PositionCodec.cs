using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BidEuchre.Core;

namespace BidEuchre.Protocol;

public sealed record BotPosition(int Seat, GameView Game);

public static class PositionCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Encode(BotPosition position)
    {
        var json = JsonSerializer.Serialize(position, JsonOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static BotPosition Decode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return JsonSerializer.Deserialize<BotPosition>(json, JsonOptions)
                ?? throw new ProtocolException("Position payload was empty.");
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ProtocolException($"Invalid position payload: {exception.Message}");
        }
    }
}
