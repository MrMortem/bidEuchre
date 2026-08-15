using BidEuchre.Core;

namespace BidEuchre.Protocol;

public abstract record BotAction
{
    public sealed record Pass : BotAction;
    public sealed record Bid(BidLevel Level) : BotAction;
    public sealed record ChooseContract(ContractMode Mode, Suit? Trump) : BotAction;
    public sealed record Exchange(Card Card) : BotAction;
    public sealed record Play(Card Card) : BotAction;
}

public static class BotActionNotation
{
    public static string Format(BotAction action) => action switch
    {
        BotAction.Pass => "bestaction pass",
        BotAction.Bid bid => $"bestaction bid {FormatBid(bid.Level)}",
        BotAction.ChooseContract contract when contract.Mode is ContractMode.Trump =>
            $"bestaction contract trump {contract.Trump!.Value.ToString().ToLowerInvariant()}",
        BotAction.ChooseContract contract =>
            $"bestaction contract {contract.Mode.ToString().ToLowerInvariant()}",
        BotAction.Exchange exchange => $"bestaction exchange {exchange.Card.Code.ToLowerInvariant()}",
        BotAction.Play play => $"bestaction play {play.Card.Code.ToLowerInvariant()}",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static BotAction Parse(string line)
    {
        var tokens = CommandTokenizer.Tokenize(line);
        if (tokens.Count < 2 || !tokens[0].Equals("bestaction", StringComparison.OrdinalIgnoreCase))
        {
            throw new ProtocolException("Expected a bestaction response.");
        }

        return tokens[1].ToLowerInvariant() switch
        {
            "pass" when tokens.Count == 2 => new BotAction.Pass(),
            "bid" when tokens.Count == 3 => new BotAction.Bid(ParseBid(tokens[2])),
            "contract" => ParseContract(tokens),
            "exchange" when tokens.Count == 3 => new BotAction.Exchange(Card.Parse(tokens[2])),
            "play" when tokens.Count == 3 => new BotAction.Play(Card.Parse(tokens[2])),
            _ => throw new ProtocolException($"Unknown or malformed bot action '{line}'.")
        };
    }

    private static BotAction ParseContract(IReadOnlyList<string> tokens)
    {
        if (tokens.Count is 3 && Enum.TryParse<ContractMode>(tokens[2], true, out var noTrumpMode) &&
            noTrumpMode is not ContractMode.Trump)
        {
            return new BotAction.ChooseContract(noTrumpMode, null);
        }

        if (tokens.Count is 4 && tokens[2].Equals("trump", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<Suit>(tokens[3], true, out var suit))
        {
            return new BotAction.ChooseContract(ContractMode.Trump, suit);
        }

        throw new ProtocolException("Contract actions use 'contract high', 'contract low', or 'contract trump <suit>'.");
    }

    private static BidLevel ParseBid(string value) => value.ToLowerInvariant() switch
    {
        "3" => BidLevel.Three,
        "4" => BidLevel.Four,
        "5" => BidLevel.Five,
        "6" => BidLevel.Six,
        "partnersbest" or "partners-best" or "pb" => BidLevel.PartnersBest,
        "alone" => BidLevel.Alone,
        _ => throw new ProtocolException($"Unknown bid '{value}'.")
    };

    private static string FormatBid(BidLevel bid) => bid switch
    {
        BidLevel.PartnersBest => "partnersbest",
        BidLevel.Alone => "alone",
        _ => ((int)bid).ToString()
    };
}

public sealed class ProtocolException(string message) : InvalidOperationException(message);
