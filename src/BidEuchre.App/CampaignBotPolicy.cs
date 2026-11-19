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

        const int humanSeat = 0;
        var pass = legal.OfType<CampaignCommand.Pass>().FirstOrDefault();
        // Give the human control of the auction until they pass. A forced dealer has no legal pass.
        if (pass is not null && seat == GameRules.PartnerOf(humanSeat) &&
            !game.Auction.Any(action => action.Seat == humanSeat && action.IsPass))
            return pass;

        var bids = legal.OfType<CampaignCommand.Bid>().Where(b => b.Variant is null).OrderBy(b => (int)b.Level).ToArray();
        if (bids.Length > 0)
        {
            // Bid what the hand is worth in its best suit, never the all-or-nothing contracts.
            var worth = (int)Math.Floor(Enum.GetValues<Suit>().Max(suit => TrumpTricks(hand, suit)) + PartnerTricks);
            var affordable = bids.LastOrDefault(b => b.Level <= BidLevel.Six && (int)b.Level <= worth);
            if (affordable is { Level: BidLevel.Three })
                return legal.OfType<CampaignCommand.Bid>().FirstOrDefault(bid => bid.Variant == GumballId.OverAchiever) ?? affordable;
            if (affordable is not null) return affordable;
            return (CampaignCommand?)pass ?? bids[0];
        }

        // A pending choice, or a pass with nothing to bid.
        return pass ?? legal[0];
    }

    private static CampaignCommand ChoosePlay(CampaignCommand.Play[] plays, IReadOnlyList<CampaignCardView> hand,
        IReadOnlyList<CampaignTrickPlay> trick, Contract contract, int seat)
    {
        CampaignCardView Card(CampaignCommand.Play play) => hand.Single(c => c.Id == play.Id);
        var low = contract.Mode is ContractMode.Low;
        // Among cards of equal power, the one least worth keeping: a low rank, or a high one when low cards win.
        int Spare(CampaignCommand.Play play) => low ? -(int)EffectiveRank(Card(play)) : (int)EffectiveRank(Card(play));

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

    private static int Power(CampaignCardView card, Suit? declaration, Contract contract, Suit? led) =>
        CampaignCardRules.TrickPower(card.Face, card.Enhancement, card.Sigil, contract, led, declaration);

    private static Rank EffectiveRank(CampaignCardView card) => CampaignCardRules.EffectiveRank(card.Face.Rank, card.Sigil);

    /// <summary>Roughly how many tricks this hand takes by itself with <paramref name="trump"/> as trump.</summary>
    private static double TrumpTricks(IReadOnlyList<CampaignCardView> hand, Suit trump)
    {
        var contract = new Contract(BidLevel.Three, ContractMode.Trump, trump);
        var tricks = 0d;
        foreach (var card in hand)
        {
            var rank = EffectiveRank(card);
            var isTrump = card.Enhancement == EnhancementId.Wild || CampaignCardRules.EffectiveSuit(card.Face, card.Enhancement, card.Sigil, contract) == trump;
            tricks += isTrump
                ? rank switch { BidEuchre.Core.Rank.Jack => 1, BidEuchre.Core.Rank.Ace => .75, BidEuchre.Core.Rank.King => .5, _ => .34 }
                : rank is BidEuchre.Core.Rank.Ace ? .6 : 0;
        }
        return tricks;
    }
}
