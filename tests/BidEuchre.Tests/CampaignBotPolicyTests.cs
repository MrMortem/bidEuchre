using BidEuchre.App;
using BidEuchre.Core;

internal static class CampaignBotPolicyTests
{
    private static Card[] Hand(string codes) => codes.Split(' ').Select(Card.Parse).ToArray();
    // An untouched campaign deck numbers its cards in the order of GameRules.CreateDeck.
    private static CardId Id(string code) { var card = Card.Parse(code); return new((int)card.Suit * 6 + (int)card.Rank - 9); }

    // East deals, so the human bids first. West holds both black bowers and the top Spades.
    private static GameEngine Table(params GumballId[] unlocks) => GameEngine.CreateFixture(new FixtureStart(
        ["You", "West", "Partner", "East"],
        [Hand("9H TH QH KH 9D TD"), Hand("JS JC AS KS QS AH"), Hand("AD KD QD JD 9C TC"), Hand("9S TS JH AC KC QC")],
        3, [0, 0],
        Campaign: CampaignSnapshot.Create(5) with { Inventory = new([], [], unlocks) },
        Options: new MatchOptions(24) { HandLimit = 8, NegativeThreshold = -24 }));

    private static CampaignCommand Choice(GameEngine game, int seat) => CampaignBotPolicy.Choose(game.CreateView(seat), seat);
    private static void Act(GameEngine game, int seat) => game.ExecuteCampaignAction(seat, Choice(game, seat));

    public static void StrongHandBids()
    {
        var game = Table();
        game.ExecuteCampaignAction(0, new CampaignCommand.Pass());
        Assert.True(Choice(game, 1) is CampaignCommand.Bid { Variant: null }, "A hand with both bowers and the top trumps must bid rather than pass.");
    }

    public static void WeakHandPasses()
    {
        var game = Table();
        game.ExecuteCampaignAction(0, new CampaignCommand.Pass());
        Act(game, 1);
        Assert.True(Choice(game, 2) is CampaignCommand.Pass, "A hand that cannot beat the high bid must pass.");
    }

    public static void BidderNamesItsLongSuit()
    {
        var game = WestWinsTheAuction();
        Assert.Equal<CampaignCommand>(new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Spades), Choice(game, 1));
    }

    public static void TrumpsAnOpponentsTrickCheaply()
    {
        var game = WestPlaysSpades();
        game.ExecuteCampaignAction(0, new CampaignCommand.Play(Id("9D")));
        // West is void in Diamonds: the Queen is the cheapest trump that takes the trick.
        Assert.Equal<CampaignCommand>(new CampaignCommand.Play(Id("QS")), Choice(game, 1));
    }

    public static void DoesNotTrumpItsPartnersTrick()
    {
        var game = WestPlaysSpades();
        game.ExecuteCampaignAction(0, new CampaignCommand.Play(Id("9D")));
        Act(game, 1);
        Act(game, 2);
        // East is void in Diamonds too, and West is already winning: throw away the weakest side card.
        Assert.Equal<CampaignCommand>(new CampaignCommand.Play(Id("JH")), Choice(game, 3));
    }

    public static void PartnerReturnsItsBestCard()
    {
        var game = Table(GumballId.PartnersBest);
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.PartnersBest));
        for (var seat = 1; seat < 4; seat++) Act(game, seat);
        game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts));
        game.ExecuteCampaignAction(0, new CampaignCommand.Exchange(Id("9D")));
        Assert.Equal(GamePhase.ExchangingPartnerCard, game.Phase);
        // In Hearts the Jack of Diamonds is the left bower, the strongest card the partner holds.
        Assert.Equal<CampaignCommand>(new CampaignCommand.Exchange(Id("JD")), Choice(game, 2));
    }

    private static GameEngine WestWinsTheAuction()
    {
        var game = Table();
        game.ExecuteCampaignAction(0, new CampaignCommand.Pass());
        for (var seat = 1; seat < 4; seat++) Act(game, seat);
        Assert.Equal(1, game.Bidder!.Value);
        return game;
    }

    private static GameEngine WestPlaysSpades()
    {
        var game = WestWinsTheAuction();
        game.ExecuteCampaignAction(1, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Spades));
        Assert.Equal(0, game.CurrentSeat!.Value);
        return game;
    }
}
