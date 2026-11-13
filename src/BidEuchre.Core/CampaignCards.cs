namespace BidEuchre.Core;

public readonly record struct CardId(int Value);
public sealed record CardState(CardId Id, Card Face, EnhancementId? Enhancement = null, SigilId? Sigil = null);
public sealed record CardOverlay(CardId Id, Card Face, EnhancementId? Enhancement, SigilId? Sigil);
public static class CampaignCards
{
    public static IReadOnlyList<CardState> CreateDeck() => GameRules.CreateDeck().Select((c, i) => new CardState(new(i), c)).ToArray();
    public static CardState ChangeSuit(CardState card, Suit suit) => Enum.IsDefined(suit)
        ? card with { Face = card.Face with { Suit = suit } } : throw new GameRuleException("Unknown suit.");
}
