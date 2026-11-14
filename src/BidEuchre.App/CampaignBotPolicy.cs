using BidEuchre.Core;
namespace BidEuchre.App;

/// <summary>How the partner and the opponents play a campaign hand. It sees only what its seat may see:
/// its own cards, the auction, the contract and the cards on the table.</summary>
public static class CampaignBotPolicy
{
    // What a hand expects from its partner when it weighs a bid.
    private const double PartnerTricks = .75;

    public static CampaignCommand Choose(GameView game, int seat)
    {
        var view = game.Campaign ?? throw new GameRuleException("This is not a campaign table.");
        var legal = view.LegalActions;
        if (legal.Count == 0) throw new GameRuleException("No campaign action is available.");
        var hand = view.Players[seat].VisibleCards;

        var plays = legal.OfType<CampaignCommand.Play>().ToArray();
        if (plays.Length > 0 && game.Contract is { } contract) return ChoosePlay(plays, hand, view.Trick, contract, seat);

        var contracts = legal.OfType<CampaignCommand.ChooseContract>().ToArray();
        if (contracts.Length > 0)
        {
            var best = Enum.GetValues<Suit>().MaxBy(suit => TrumpTricks(hand, suit));
            return contracts.FirstOrDefault(c => c.Mode == ContractMode.Trump && c.Trump == best) ?? contracts[0];
        }

        var exchanges = legal.OfType<CampaignCommand.Exchange>().ToArray();
        if (exchanges.Length > 0 && game.Contract is { } exchange)
        {
            // The bidder passes its weakest card; its partner answers with the best one it holds.
            int Worth(CampaignCommand.Exchange e) => Power(hand.Single(c => c.Id == e.Id), null, exchange, null);
            return game.Phase is GamePhase.ExchangingPartnerCard ? exchanges.MaxBy(Worth)! : exchanges.MinBy(Worth)!;
        }

        var bids = legal.OfType<CampaignCommand.Bid>().Where(b => b.Variant is null).OrderBy(b => (int)b.Level).ToArray();
        if (bids.Length > 0)
        {
            // Bid what the hand is worth in its best suit, never the all-or-nothing contracts.
            var worth = (int)Math.Floor(Enum.GetValues<Suit>().Max(suit => TrumpTricks(hand, suit)) + PartnerTricks);
            var affordable = bids.LastOrDefault(b => b.Level <= BidLevel.Six && (int)b.Level <= worth);
            if (affordable is not null) return affordable;
            return legal.OfType<CampaignCommand.Pass>().FirstOrDefault() as CampaignCommand ?? bids[0];
        }

        // A pending choice, or a pass with nothing to bid.
        return legal.OfType<CampaignCommand.Pass>().FirstOrDefault() as CampaignCommand ?? legal[0];
    }

    private static CampaignCommand ChoosePlay(CampaignCommand.Play[] plays, IReadOnlyList<CampaignCardView> hand,
        IReadOnlyList<CampaignTrickPlay> trick, Contract contract, int seat)
    {
        CampaignCardView Card(CampaignCommand.Play play) => hand.Single(c => c.Id == play.Id);
        var low = contract.Mode is ContractMode.Low;
        // Among cards of equal power, the one least worth keeping: a low rank, or a high one when low cards win.
        int Spare(CampaignCommand.Play play) => low ? -(int)Rank(Card(play)) : (int)Rank(Card(play));

        if (trick.Count == 0)
        {
            // Lead the card most likely to hold the trick.
            return plays.OrderByDescending(play => Power(Card(play), play.EffectiveSuit, contract, null)).ThenBy(Spare).First();
        }

        var led = trick[0].EffectiveSuit;
        var winning = trick.MaxBy(play => Power(play.Card, play.EffectiveSuit, contract, led))!;
        var toBeat = Power(winning.Card, winning.EffectiveSuit, contract, led);
        int Strength(CampaignCommand.Play play) => Power(Card(play), play.EffectiveSuit, contract, led);

        var winners = plays.Where(play => Strength(play) > toBeat).ToArray();
        if (winning.Seat % 2 != seat % 2 && winners.Length > 0)
            return winners.OrderBy(Strength).ThenBy(Spare).First();
        // The partner already has it, or it cannot be taken: keep the good cards.
        return plays.OrderBy(Strength).ThenBy(Spare).First();
    }

    /// <summary>How strongly a card competes for a trick, on the scale the engine decides tricks with.
    /// <paramref name="led"/> is null for a card that is led or only being weighed.</summary>
    private static int Power(CampaignCardView card, Suit? declared, Contract contract, Suit? led)
    {
        var rank = Rank(card);
        var face = card.Face with { Rank = rank };
        var suit = declared ?? GameRules.EffectiveSuit(face, contract);
        if (contract.Mode is ContractMode.Trump && suit == contract.Trump)
            return rank is BidEuchre.Core.Rank.Jack ? card.Enhancement == EnhancementId.Wild || face.Suit == contract.Trump ? 400 : 399 : 200 + (int)rank;
        if (led is { } follow && suit != follow) return int.MinValue;
        return contract.Mode is ContractMode.Low ? 100 - (int)rank : 100 + (int)rank;
    }

    // A Bear card counts one rank higher, and an Ace wraps to a 9.
    private static Rank Rank(CampaignCardView card) => card.Sigil != SigilId.Bear ? card.Face.Rank
        : card.Face.Rank is BidEuchre.Core.Rank.Ace ? BidEuchre.Core.Rank.Nine : card.Face.Rank + 1;

    /// <summary>Roughly how many tricks this hand takes by itself with <paramref name="trump"/> as trump.</summary>
    private static double TrumpTricks(IReadOnlyList<CampaignCardView> hand, Suit trump)
    {
        var contract = new Contract(BidLevel.Three, ContractMode.Trump, trump);
        var tricks = 0d;
        foreach (var card in hand)
        {
            var rank = Rank(card);
            var isTrump = card.Enhancement == EnhancementId.Wild || GameRules.EffectiveSuit(card.Face with { Rank = rank }, contract) == trump;
            tricks += isTrump
                ? rank switch { BidEuchre.Core.Rank.Jack => 1, BidEuchre.Core.Rank.Ace => .75, BidEuchre.Core.Rank.King => .5, _ => .34 }
                : rank is BidEuchre.Core.Rank.Ace ? .6 : 0;
        }
        return tricks;
    }
}
