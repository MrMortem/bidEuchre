namespace BidEuchre.Core;

public sealed record ContractKey(ContractMode Mode, Suit? Trump);
public sealed record CampaignSnapshot(long Revision, CardState[] Deck, CampaignInventory Inventory,
    int[] Wallets, ulong RandomState, ContractKey? PreviousContract = null)
{
    public TarotId[][] GeneratedTarot { get; init; } = [[], [], [], []];
    public static CampaignSnapshot Create(ulong seed) => new(0, CampaignCards.CreateDeck().ToArray(), CampaignInventory.Empty, new int[4], seed);
    public CampaignSnapshot Copy() => this with { Deck = Deck.ToArray(), Inventory = Inventory.Copy(), Wallets = Wallets.ToArray(), GeneratedTarot = GeneratedTarot.Select(cards => cards.ToArray()).ToArray() };
    public bool IsValid() => GeneratedTarot is { Length: 4 } && GeneratedTarot.All(cards => cards is { Length: <= 2 } && cards.All(Enum.IsDefined)) && Revision >= 0 && Deck is { Length: 24 } && Inventory is not null
        && Wallets is { Length: 4 } && Wallets.All(m => m >= 0)
        && Deck.All(c => c is not null && c.Id.Value is >= 0 and < 24 && Enum.IsDefined(c.Face.Suit) && Enum.IsDefined(c.Face.Rank)
            && (c.Enhancement is null || Enum.IsDefined(c.Enhancement.Value)) && (c.Sigil is null || Enum.IsDefined(c.Sigil.Value)))
        && Deck.Select(c => c.Id).Distinct().Count() == 24 && Deck.Count(c => c.Enhancement == EnhancementId.Stinky) <= 18
        && Inventory.Jokers is { Length: <= 5 } && Inventory.Tarot is not null && Inventory.Unlocks is not null
        && Inventory.Jokers.All(j => j is not null && Enum.IsDefined(j.Id)) && Inventory.Jokers.Select(j => j.Id).Distinct().Count() == Inventory.Jokers.Length
        && Inventory.Tarot.All(Enum.IsDefined) && Inventory.Unlocks.All(Enum.IsDefined)
        && Inventory.Sigils is not null && Inventory.Sigils.All(Enum.IsDefined)
        && Inventory.Unlocks.Distinct().Count() == Inventory.Unlocks.Length;
}
public sealed class CampaignContext
{
    private CampaignSnapshot _state;
    public CampaignContext(CampaignSnapshot snapshot)
    {
        if (!snapshot.IsValid()) throw new GameRuleException("Invalid campaign resources.");
        _state = snapshot.Copy();
    }
    public CampaignSnapshot Snapshot() => _state.Copy();
    internal void Replace(CampaignSnapshot state)
    {
        if (!state.IsValid()) throw new GameRuleException("Invalid campaign resources.");
        _state = state.Copy();
    }
}
