using System.Text;
using System.Text.Json.Nodes;
using BidEuchre.Core;
using BidEuchre.Protocol;

internal static class HybridFixtureTests
{
    public static void DealSerialSurvivesRedeal()
    {
        var game = new GameEngine(["North", "East", "South", "West"], randomSeed: 17);
        Assert.Equal(0L, game.DealSerial);
        game.StartGame(dealer: 3);
        var first = game.CreateView(0).DealSerial;
        Assert.Equal(1L, first!.Value);
        game.PlaceBid(0, BidLevel.Three);
        game.Redeal();
        Assert.Equal(first!.Value + 1, game.CreateView(0).DealSerial!.Value);
        Assert.Equal(1, game.HandNumber);
        Assert.Equal(0, game.Auction.Count);
        Assert.Equal(3, game.Dealer);
        game.Redeal();
        Assert.Equal(3L, game.DealSerial);
        Assert.Equal(1, game.HandNumber);
    }

    public static void LegacyViewHasUnknownSerial()
    {
        var game = GameEngine.CreateFixture(Start());
        var payload = PositionCodec.Encode(new BotPosition(0, game.CreateView(0)));
        var base64 = payload.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        var json = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)))!;
        Assert.True(json["game"]!.AsObject().Remove("dealSerial"));
        var legacyPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json.ToJsonString()))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.True(PositionCodec.Decode(legacyPayload).Game.DealSerial is null);
    }

    public static void DealSerialRoundTrips()
    {
        var game = GameEngine.CreateFixture(Start() with { DealSerial = 4567890123L });
        var position = new BotPosition(0, game.CreateView(0));
        Assert.Equal(4567890123L, PositionCodec.Decode(PositionCodec.Encode(position)).Game.DealSerial!.Value);
    }

    public static void FixtureRejectsDuplicateCards()
    {
        var duplicateCardStart = Start();
        duplicateCardStart.Hands[1][0] = duplicateCardStart.Hands[0][0];
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(duplicateCardStart));
    }

    public static void FixtureCopiesInputs()
    {
        var start = Start() with { Scores = [-12, 39] };
        var game = GameEngine.CreateFixture(start);
        start.Names[0] = "Changed";
        start.Scores[0] = 100;
        start.Hands[0][0] = Card.Parse("AS");
        start.Hands[1] = [];
        var view = game.CreateView(0, revealAllCards: true);
        Assert.Equal("North", view.Players[0].Name);
        Assert.Equal(-12, view.Scores[0]);
        Assert.Equal(39, view.Scores[1]);
        Assert.Equal(6, view.Players[1].CardCount);
        Assert.Contains(Card.Parse("9C"), view.Players[0].Cards!);
        Assert.False(view.Players[0].Cards!.Contains(Card.Parse("AS")));
        Assert.Equal(24, view.Players.SelectMany(player => player.Cards!).Distinct().Count());
    }

    public static void FixtureRequiresFourSixCardHands()
    {
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { Hands = [] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { Hands = null! }));
        var missing = Start();
        missing.Hands[0] = missing.Hands[0][..5];
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(missing));
        var extra = Start();
        extra.Hands[0] = [.. extra.Hands[0], Card.Parse("AS")];
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(extra));
        var nullHand = Start();
        nullHand.Hands[0] = null!;
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(nullHand));
    }

    public static void FixtureRejectsInvalidCards()
    {
        var invalidSuit = Start();
        invalidSuit.Hands[0][0] = new Card((Suit)99, Rank.Nine);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(invalidSuit));
        var invalidRank = Start();
        invalidRank.Hands[0][0] = new Card(Suit.Clubs, (Rank)2);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(invalidRank));
    }

    public static void FixtureValidatesNamesAndDealer()
    {
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { Names = [] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { Names = null! }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { Names = ["N", "E", " ", "W"] }));
        foreach (var dealer in new[] { -1, 4 })
            Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { Dealer = dealer }));
    }

    public static void FixtureValidatesScoresAndIdentity()
    {
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { Scores = [0] }));
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { Scores = null! }));
        foreach (var scores in new[] { new[] { 40, 39 }, new[] { 39, 40 } })
            Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { Scores = scores }));
        foreach (var value in new[] { 0, -1 })
        {
            Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { HandNumber = value }));
            Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(Start() with { DealSerial = value }));
        }
        Assert.Equal(GamePhase.Bidding, GameEngine.CreateFixture(Start() with { Scores = [40, 40] }).Phase);
        Assert.Equal(-24, GameEngine.CreateFixture(Start() with { Scores = [-24, -12] }).Scores[0]);
        Assert.Throws<ArgumentException>(() => GameEngine.CreateFixture(null!));
    }

    public static void FixtureStartsAtLegalBiddingBoundary()
    {
        var game = GameEngine.CreateFixture(Start() with { Dealer = 1, HandNumber = 7, DealSerial = 23 });
        Assert.Equal(GamePhase.Bidding, game.Phase);
        Assert.Equal(2, game.CurrentSeat!.Value);
        Assert.Equal(1, game.Dealer);
        Assert.Equal(7, game.HandNumber);
        Assert.Equal(23L, game.DealSerial);
        Assert.True(game.HighBid is null && game.Bidder is null && game.Contract is null && game.GameWinner is null);
        Assert.Equal(0, game.Auction.Count);
        Assert.Equal(0, game.CurrentTrick.Count);
        Assert.Equal(0, game.CompletedTricks.Count);
        Assert.Equal(0, game.HandHistory.Count);
        Assert.True(game.TricksByTeam.All(count => count == 0));
        Assert.True(game.CreateView(2).LegalActions.CanPass);
        Assert.True(game.CreateView(2).Players[0].Cards is null);
        foreach (var seat in new[] { 2, 3, 0 }) game.PlaceBid(seat, null);
        Assert.False(game.CanPass);
        Assert.Throws<GameRuleException>(() => game.PlaceBid(1, null));
        game.PlaceBid(1, BidLevel.Three);
        Assert.Equal(GamePhase.ChoosingContract, game.Phase);
        game.ChooseContract(1, ContractMode.Trump, Suit.Diamonds);
        Assert.Equal(GamePhase.Playing, game.Phase);
        while (game.Phase is GamePhase.Playing)
            game.PlayCard(game.CurrentSeat!.Value, game.GetLegalCards(game.CurrentSeat.Value)[0]);
        game.StartNextHand();
        Assert.Equal(8, game.HandNumber);
        Assert.Equal(24L, game.DealSerial);
        Assert.Equal(2, game.Dealer);
    }

    public static void FixtureNearFortyCompletesGame()
    {
        var game = GameEngine.CreateFixture(Start() with { Scores = [39, -12] });
        game.PlaceBid(0, BidLevel.Three);
        foreach (var seat in new[] { 1, 2, 3 }) game.PlaceBid(seat, null);
        game.ChooseContract(0, ContractMode.Trump, Suit.Clubs);
        game.PlayCard(0, Card.Parse("JC")); // Draw the Left Bower before leading the other clubs.
        while (game.Phase is GamePhase.Playing)
            game.PlayCard(game.CurrentSeat!.Value, game.GetLegalCards(game.CurrentSeat.Value)[0]);
        Assert.Equal(GamePhase.GameComplete, game.Phase);
        Assert.Equal(0, game.GameWinner!.Value);
        Assert.Equal(45, game.Scores[0]);
        Assert.Equal(-12, game.Scores[1]);
        Assert.Equal(1L, game.DealSerial);
        Assert.Throws<GameRuleException>(() => game.StartNextHand());
    }

    private static FixtureStart Start() => new(
        ["North", "East", "South", "West"],
        [
            [Card.Parse("9C"), Card.Parse("TC"), Card.Parse("JC"), Card.Parse("QC"), Card.Parse("KC"), Card.Parse("AC")],
            [Card.Parse("9D"), Card.Parse("TD"), Card.Parse("JD"), Card.Parse("QD"), Card.Parse("KD"), Card.Parse("AD")],
            [Card.Parse("9H"), Card.Parse("TH"), Card.Parse("JH"), Card.Parse("QH"), Card.Parse("KH"), Card.Parse("AH")],
            [Card.Parse("9S"), Card.Parse("TS"), Card.Parse("JS"), Card.Parse("QS"), Card.Parse("KS"), Card.Parse("AS")]
        ],
        Dealer: 3,
        Scores: [0, 0]);
}
