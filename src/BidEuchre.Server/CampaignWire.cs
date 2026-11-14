using System.Text.Json;
using BidEuchre.Core;

namespace BidEuchre.Server;

public sealed class CampaignApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public static CampaignApiException Invalid(string message = "Invalid campaign request.") => new(400, "invalid_request", message);
    public static CampaignApiException Stale() => new(409, "stale_state", "The table changed; choose again.");
    public static CampaignApiException Phase() => new(409, "wrong_phase", "This action is not available in the current campaign phase.");
}

// Deliberately separate from save JSON: strict names, enums, bounds and a closed command union.
public static class CampaignWire
{
    public static void Fields(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw CampaignApiException.Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal))
                throw CampaignApiException.Invalid("Unknown or duplicate campaign request field.");
    }
    public static JsonElement Required(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result)
        ? result : throw CampaignApiException.Invalid($"Missing {name}.");
    public static string Text(JsonElement value, string name, int max = 128)
    {
        var item = Required(value, name);
        return item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text && text.Length <= max
            ? text : throw CampaignApiException.Invalid($"Invalid {name}.");
    }
    public static long Number(JsonElement value, string name, long min = 0, long max = long.MaxValue)
    {
        var item = Required(value, name);
        return item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var number) && number >= min && number <= max
            ? number : throw CampaignApiException.Invalid($"Invalid {name}.");
    }
    public static T EnumValue<T>(JsonElement value, string name) where T : struct, Enum
    {
        var text = Text(value, name);
        return Enum.TryParse<T>(text, false, out var parsed) && Enum.IsDefined(parsed) && parsed.ToString() == text
            ? parsed : throw CampaignApiException.Invalid($"Invalid {name}.");
    }
    private static T? Optional<T>(JsonElement value, string name) where T : struct, Enum =>
        !value.TryGetProperty(name, out var item) || item.ValueKind == JsonValueKind.Null ? null : EnumValue<T>(value, name);
    public static CardId[] Cards(JsonElement value, string name)
    {
        var items = Required(value, name);
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 24) throw CampaignApiException.Invalid($"Invalid {name}.");
        var ids = items.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var n) && n is >= 0 and < 24
            ? new CardId(n) : throw CampaignApiException.Invalid("Invalid physical card ID.")).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw CampaignApiException.Invalid("Choose different physical cards.");
        return ids;
    }
    public static CampaignCommand ParseCommand(JsonElement value)
    {
        var kind = Text(value, "kind");
        var fields = kind switch
        {
            "bid" => new[] { "level", "variant" }, "pass" or "panic-button" => [],
            "play" => ["cardId", "effectiveSuit"], "exchange" or "swap-partner-card" or "reveal-partner-card" => ["cardId"],
            "choose-contract" => ["mode", "trump"], "use-tarot" => ["slot", "targetIds"],
            "use-sigil" => ["slot", "targetId"], "choose-reveal-seat" or "select-opponent" => ["targetSeat"],
            _ => throw CampaignApiException.Invalid("Unknown campaign command kind.")
        };
        Fields(value, ["kind", .. fields]);
        CardId Id(string name = "cardId") => new((int)Number(value, name, 0, 23));
        int Slot() => (int)Number(value, "slot", 0, CampaignInventory.ItemSlots - 1);
        int Seat() => (int)Number(value, "targetSeat", 0, 3);
        return kind switch
        {
            "bid" => new CampaignCommand.Bid(EnumValue<BidLevel>(value, "level"), Optional<GumballId>(value, "variant")),
            "pass" => new CampaignCommand.Pass(), "panic-button" => new CampaignCommand.PanicButton(),
            "play" => new CampaignCommand.Play(Id(), Optional<Suit>(value, "effectiveSuit")),
            "exchange" => new CampaignCommand.Exchange(Id()),
            "choose-contract" => ContractCommand(value),
            "use-tarot" => new CampaignCommand.UseTarot(Slot(), Cards(value, "targetIds")),
            "use-sigil" => new CampaignCommand.UseSigil(Slot(), Id("targetId")),
            "swap-partner-card" => new CampaignCommand.SwapPartnerCard(Id()),
            "choose-reveal-seat" => new CampaignCommand.ChooseRevealSeat(Seat()),
            "reveal-partner-card" => new CampaignCommand.RevealPartnerCard(Id()),
            "select-opponent" => new CampaignCommand.SelectOpponent(Seat()),
            _ => throw CampaignApiException.Invalid()
        };
    }
    private static CampaignCommand ContractCommand(JsonElement value)
    {
        var mode = EnumValue<ContractMode>(value, "mode"); var trump = Optional<Suit>(value, "trump");
        if ((mode == ContractMode.Trump) != (trump is not null)) throw CampaignApiException.Invalid("Trump requires a suit; High and Low do not take a suit.");
        return new CampaignCommand.ChooseContract(mode, trump);
    }
    public static object Command(CampaignCommand command) => command switch
    {
        CampaignCommand.Bid c => new { kind = "bid", level = c.Level.ToString(), variant = c.Variant?.ToString() },
        CampaignCommand.Pass => new { kind = "pass" }, CampaignCommand.PanicButton => new { kind = "panic-button" },
        CampaignCommand.Play c => new { kind = "play", cardId = c.Id.Value, effectiveSuit = c.EffectiveSuit?.ToString() },
        CampaignCommand.Exchange c => new { kind = "exchange", cardId = c.Id.Value },
        CampaignCommand.ChooseContract c => new { kind = "choose-contract", mode = c.Mode.ToString(), trump = c.Trump?.ToString() },
        CampaignCommand.UseTarot c => new { kind = "use-tarot", slot = c.Slot, targetIds = c.Targets.Select(id => id.Value).ToArray() },
        CampaignCommand.UseSigil c => new { kind = "use-sigil", slot = c.Slot, targetId = c.Target.Value },
        CampaignCommand.SwapPartnerCard c => new { kind = "swap-partner-card", cardId = c.Id.Value },
        CampaignCommand.ChooseRevealSeat c => new { kind = "choose-reveal-seat", targetSeat = c.Seat },
        CampaignCommand.RevealPartnerCard c => new { kind = "reveal-partner-card", cardId = c.Id.Value },
        CampaignCommand.SelectOpponent c => new { kind = "select-opponent", targetSeat = c.Seat },
        _ => throw new ArgumentException("Unsupported campaign command.")
    };
}
