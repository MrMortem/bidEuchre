using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Protocol;

internal static class FreePlayBotPolicyTests
{
    private static Card[] Cards(string codes) => codes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Card.Parse).ToArray();
    private static readonly BuiltInBotDriver Bot = new();
    private static BotAction Choose(GameView view, int seat = 0) => Bot.ChooseActionAsync(view, seat).GetAwaiter().GetResult();

    private static GameView View(GamePhase phase, string hand, Contract? contract = null,
        IReadOnlyList<CardPlay>? trick = null, int seat = 0, BidLevel? highBid = null,
        bool canPass = true, BidLevel[]? bids = null, ContractMode[]? modes = null, Suit[]? suits = null)
    {
        var cards = Cards(hand);
        trick ??= [];
        return new GameView(phase, 1, 3, seat, [0, 0],
            Enumerable.Range(0, 4).Select(s => new PlayerView(s, $"Seat {s}", s % 2, s == seat ? cards.Length : 6,
                s == seat ? cards : null, false)).ToArray(), [], highBid, highBid is null ? null : 1,
            contract, trick, [], [0, 0], null,
            new LegalActionView(canPass, bids ?? GameRules.LegalRaises(highBid).ToArray(),
                modes ?? [ContractMode.Trump, ContractMode.High, ContractMode.Low], suits ?? Enum.GetValues<Suit>(),
                phase is GamePhase.Playing && contract is not null ? GameRules.LegalCards(cards, trick, contract) : cards), []);
    }

    public static void UnrelatedJackDoesNotCreateTrumpLength()
    {
        Assert.True(Choose(View(GamePhase.Bidding, "9H TH JS QC KD AS")) is BotAction.Pass,
            "A black Jack must not inflate a weak red suit into a three-trick bid.");
    }

    public static void TrumpBeatsAnOffSuitAce()
    {
        var view = View(GamePhase.Playing, "9H AS", new(BidLevel.Three, ContractMode.Trump, Suit.Hearts),
            [new(1, Card.Parse("AD"))]);
        Assert.Equal<BotAction>(new BotAction.Play(Card.Parse("9H")), Choose(view));
    }

    public static void LeftBowerFollowsTrump()
    {
        var view = View(GamePhase.Playing, "JD AH AS", new(BidLevel.Three, ContractMode.Trump, Suit.Hearts),
            [new(1, Card.Parse("JH"))]);
        // Both Hearts and its left bower follow. The Ace loses more cheaply to the right bower.
        Assert.Equal<BotAction>(new BotAction.Play(Card.Parse("AH")), Choose(view));
        view = View(GamePhase.Playing, "JD AS", new(BidLevel.Three, ContractMode.Trump, Suit.Hearts),
            [new(1, Card.Parse("AD"))]);
        Assert.Equal<BotAction>(new BotAction.Play(Card.Parse("JD")), Choose(view));
    }

    public static void SavesCardsWhenPartnerIsWinning()
    {
        var view = View(GamePhase.Playing, "9D AD JH", new(BidLevel.Three, ContractMode.Trump, Suit.Hearts),
            [new(1, Card.Parse("QD")), new(2, Card.Parse("KD")), new(3, Card.Parse("TD"))]);
        Assert.Equal<BotAction>(new BotAction.Play(Card.Parse("9D")), Choose(view));
    }

    public static void UsesLowestWinnerAndDiscardsCheaply()
    {
        var contract = new Contract(BidLevel.Three, ContractMode.Trump, Suit.Hearts);
        var view = View(GamePhase.Playing, "9H AH JH AS", contract, [new(1, Card.Parse("AD"))]);
        Assert.Equal<BotAction>(new BotAction.Play(Card.Parse("9H")), Choose(view));
        view = View(GamePhase.Playing, "9D KD AD", contract, [new(1, Card.Parse("QD"))]);
        Assert.Equal<BotAction>(new BotAction.Play(Card.Parse("KD")), Choose(view));
        view = View(GamePhase.Playing, "9D AD", contract, [new(1, Card.Parse("9H")), new(2, Card.Parse("TD"))]);
        Assert.Equal<BotAction>(new BotAction.Play(Card.Parse("9D")), Choose(view));
        view = View(GamePhase.Playing, "AD KS", contract, [new(1, Card.Parse("JH"))]);
        Assert.Equal<BotAction>(new BotAction.Play(Card.Parse("KS")), Choose(view));
    }

    public static void LowUsesLowestWinningRankAndSavesNine()
    {
        var contract = new Contract(BidLevel.Four, ContractMode.Low, null);
        var view = View(GamePhase.Playing, "9D JD AD", contract, [new(1, Card.Parse("QD"))]);
        Assert.Equal<BotAction>(new BotAction.Play(Card.Parse("JD")), Choose(view));
        view = View(GamePhase.Playing, "9D JD AD", contract,
            [new(1, Card.Parse("KD")), new(2, Card.Parse("TD")), new(3, Card.Parse("QD"))]);
        Assert.Equal<BotAction>(new BotAction.Play(Card.Parse("AD")), Choose(view));
    }

    public static void StrongHandRaisesAndWeakHandPasses()
    {
        var action = Choose(View(GamePhase.Bidding, "JS JC AS KS 9D TH", highBid: BidLevel.Three));
        Assert.True(action is BotAction.Bid { Level: >= BidLevel.Four and <= BidLevel.Five },
            "Both bowers and two top trumps justify a numeric raise, not an all-tricks contract.");
        Assert.True(Choose(View(GamePhase.Bidding, "9H TH JS QC KD AS", highBid: BidLevel.Four)) is BotAction.Pass,
            "A weak hand must not chase a high bid.");
    }

    public static void ForcedDealerUsesALegalBid()
    {
        var game = Table("9H TH JS QC KD AS");
        for (var seat = 1; seat < 4; seat++) game.PlaceBid(seat, null);
        var view = game.CreateView(0);
        Assert.True(!view.LegalActions.CanPass, "Dealer fixture must be forced to bid.");
        var action = Choose(view);
        Assert.True(action is BotAction.Bid bid && view.LegalActions.Bids.Contains(bid.Level), "Forced bid must be legal.");
        Apply(game, action);
    }

    public static void HighAndLowEvaluateTheirOwnRanks()
    {
        foreach (var (hand, mode) in new[] { ("AC QC AD KD AH AS", ContractMode.High), ("9C JC 9D TD 9H 9S", ContractMode.Low) })
        {
            Assert.True(Choose(View(GamePhase.Bidding, hand, highBid: BidLevel.Three)) is BotAction.Bid { Level: >= BidLevel.Four and <= BidLevel.Six },
                $"A strong {mode} hand should raise into a legal no-trump contract.");
            Assert.Equal<BotAction>(new BotAction.ChooseContract(mode, null),
                Choose(View(GamePhase.ChoosingContract, hand, highBid: BidLevel.Four)));
        }
        var restricted = View(GamePhase.ChoosingContract, "AC QC AD KD AH AS", highBid: BidLevel.Four,
            modes: [ContractMode.Trump], suits: [Suit.Diamonds]);
        Assert.Equal<BotAction>(new BotAction.ChooseContract(ContractMode.Trump, Suit.Diamonds), Choose(restricted));
    }

    public static void SpecialBidsNeedControlOfTheHand()
    {
        Assert.Equal<BotAction>(new BotAction.Bid(BidLevel.Alone), Choose(View(GamePhase.Bidding, "JS JC AS KS QS TS")));
        Assert.Equal<BotAction>(new BotAction.Bid(BidLevel.PartnersBest), Choose(View(GamePhase.Bidding, "JS JC AS KS QS 9H")));
        var restricted = View(GamePhase.Bidding, "JS JC AS KS QS TS", bids: [BidLevel.Four]);
        Assert.Equal<BotAction>(new BotAction.Bid(BidLevel.Four), Choose(restricted));
    }

    public static void ExchangeUsesContractStrengthAndLegalCards()
    {
        var game = Table("9D AH JH 9C TC QC");
        for (var seat = 1; seat < 4; seat++) game.PlaceBid(seat, null);
        game.PlaceBid(0, BidLevel.PartnersBest);
        game.ChooseContract(0, ContractMode.Trump, Suit.Hearts);
        var bidder = Choose(game.CreateView(0));
        Assert.Equal<BotAction>(new BotAction.Exchange(Card.Parse("9C")), bidder);
        Apply(game, bidder);
        // The partner's visible seven-card hand includes a left bower, which outranks every printed Ace.
        var partner = game.CreateView(2);
        var answer = Choose(partner, 2);
        Assert.True(answer is BotAction.Exchange exchange && partner.LegalActions.Cards.Contains(exchange.Card),
            "The partner must return a card from its actual legal hand.");
        Apply(game, answer);
        Assert.Equal(GamePhase.Playing, game.Phase);
        Assert.Equal(6, game.CreateView(0).Players.Single(p => p.Seat == 0).CardCount);
        var cards = Cards("JD AS KS QS 9S TS AD");
        partner = partner with { Players = partner.Players.Select(p => p.Seat == 2 ? p with { Cards = cards } : p).ToArray(),
            LegalActions = partner.LegalActions with { Cards = cards } };
        Assert.Equal<BotAction>(new BotAction.Exchange(Card.Parse("JD")), Choose(partner, 2));
        var limited = partner with { LegalActions = partner.LegalActions with { Cards = Cards("KS QS") } };
        Assert.Equal<BotAction>(new BotAction.Exchange(Card.Parse("KS")), Choose(limited, 2));
    }

    public static void HiddenHandsCannotChangeDecisions()
    {
        var view = View(GamePhase.Bidding, "JS JC AS KS 9D TH");
        var revealed = view with { Players = view.Players.Select(p => p.Seat == 0 ? p : p with { Cards = Cards("AH KH QH JH 9H TH") }).ToArray() };
        Assert.Equal(Choose(view), Choose(revealed));
    }

    public static void FullBotTablesComplete()
    {
        foreach (var hand in new[] { "JS JC AS KS QS TS", "JS JC AS KS QS 9H" })
        {
            var special = Table(hand);
            for (var seat = 1; seat < 4; seat++) special.PlaceBid(seat, null);
            var guard = 40;
            while (special.Phase != GamePhase.HandComplete && guard-- > 0)
            {
                var seat = special.CurrentSeat!.Value;
                Apply(special, Choose(special.CreateView(seat), seat));
            }
            Assert.True(guard > 0, "A special-contract table must finish its hand, including any exchange.");
            Assert.Equal(GamePhase.HandComplete, special.Phase);
        }

        for (var seed = 0; seed < 12; seed++)
        {
            var game = new GameEngine(["North", "East", "South", "West"], seed, new MatchOptions(12));
            game.StartGame(seed % 4);
            var actions = 0;
            while (game.Phase != GamePhase.GameComplete && actions++ < 6000)
            {
                if (game.Phase == GamePhase.HandComplete) { game.StartNextHand(); continue; }
                var seat = game.CurrentSeat!.Value;
                var view = game.CreateView(seat);
                Assert.True(view.Players.Where(p => p.Seat != seat).All(p => p.Cards is null), "Simulation must keep other hands private.");
                Apply(game, Choose(view, seat));
            }
            Assert.Equal(GamePhase.GameComplete, game.Phase);
        }
    }

    private static GameEngine Table(string ownHand)
    {
        var own = Cards(ownHand);
        var rest = GameRules.CreateDeck().Except(own).ToArray();
        return GameEngine.CreateFixture(new FixtureStart(["N", "E", "S", "W"],
            [own, rest[..6], rest[6..12], rest[12..]], 0, [0, 0]));
    }

    private static void Apply(GameEngine game, BotAction action)
    {
        var seat = game.CurrentSeat!.Value;
        switch (action)
        {
            case BotAction.Pass: game.PlaceBid(seat, null); break;
            case BotAction.Bid bid: game.PlaceBid(seat, bid.Level); break;
            case BotAction.ChooseContract contract: game.ChooseContract(seat, contract.Mode, contract.Trump); break;
            case BotAction.Exchange exchange: game.ExchangeCard(seat, exchange.Card); break;
            case BotAction.Play play: game.PlayCard(seat, play.Card); break;
            default: throw new InvalidOperationException("Unexpected bot action.");
        }
    }
}
