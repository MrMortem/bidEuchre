using System.Text.Json.Nodes;
using BidEuchre.App;
using BidEuchre.Core;

internal static class PokerSessionTests
{
    private static GameEngine Auction()
    {
        var tokens = GameRules.CreateDeck().ToArray();
        var resources = CampaignSnapshot.Create(41) with
        {
            Inventory = new([], [], [GumballId.Poker]),
            Deck = CampaignCards.CreateDeck().Select(c => c with
            { Face = new Card(Suit.Clubs, c.Id.Value < 5 ? Rank.Ace : Rank.Nine) }).ToArray()
        };
        return GameEngine.CreateFixture(new FixtureStart(["You", "West", "Partner", "East"],
            Enumerable.Range(0, 4).Select(s => tokens.Skip(s * 6).Take(6).ToArray()).ToArray(),
            3, [0, 0], Campaign: resources, Options: new MatchOptions(1000)));
    }

    public static async Task RevealResumeAndRedeal()
    {
        var game = Auction();
        var seats = game.ExportCampaignMatchSnapshot().PlayerNames.Select((name, seat) => new SeatConfiguration(seat, name, PlayerKind.Human, null)).ToArray();
        CampaignCheckpoint? checkpoint = null;
        await using var session = new GameSession("poker-session", "Poker", seats, new EngineCatalog(), 41,
            botActionDelay: TimeSpan.Zero, waitForContractPresentation: true,
            liveCheckpoint: c => { checkpoint = c; return true; });
        await session.RestoreCampaignAsync(new(game.ExportCampaignMatchSnapshot()));
        var order = game.ExportCampaignMatchSnapshot().Hands[0].Reverse().ToArray();
        await session.ArrangeCampaignHandAsync(0, order);
        async Task Act(int seat, CampaignCommand command)
        {
            var view = (await session.GetViewAsync(seat))!.Campaign!;
            await session.ExecuteCampaignAsync(new(seat, view.Revision, view.DealSerial, command));
        }
        await Act(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Poker));
        for (var seat = 1; seat < 4; seat++) await Act(seat, new CampaignCommand.Pass());
        Assert.Equal("PokerReveal", session.Game!.Phase.ToString());
        Assert.False(session.WaitingForContractPresentation);
        Assert.Equal(4, session.Game.Scores[0]);
        Assert.True(checkpoint!.IsValid());

        await using var resumed = new GameSession("poker-resumed", "Poker", seats, new EngineCatalog(), 41,
            botActionDelay: TimeSpan.Zero, waitForContractPresentation: true,
            liveCheckpoint: c => { checkpoint = c; return true; });
        await resumed.RestoreCampaignAsync(checkpoint);
        Assert.Equal(4, resumed.Game!.Scores[0]);
        Assert.True((await resumed.GetViewAsync(0))!.Campaign!.Players.All(p => p.VisibleCards.Count == 6));
        Assert.False(resumed.WaitingForContractPresentation);
        var reveal = (await resumed.GetViewAsync(0))!.Campaign!;
        var request = new CampaignActionRequest(0, reveal.Revision, reveal.DealSerial, new CampaignCommand.CompletePoker());
        await resumed.ExecuteCampaignAsync(request, waitForDeal: true);
        Assert.Equal(GamePhase.Bidding, resumed.Game.Phase);
        Assert.Equal(reveal.DealSerial + 1, resumed.Game.DealSerial);
        Assert.Equal(4, resumed.Game.Scores[0]);
        Assert.True(resumed.WaitingForDeal, "Automatic Poker redeal must wait for desktop's deal animation when requested.");
        Assert.True(checkpoint!.Arrangement is null);
        await Assert.ThrowsAsync<GameRuleException>(() => resumed.ExecuteCampaignAsync(request));
        await resumed.CompleteDealAsync(resumed.Game.HandNumber);
        Assert.False(resumed.WaitingForDeal);
        Assert.True(checkpoint.IsValid());
    }

    public static Task SavedRevealRejectsMissingWinningCardIdentity()
    {
        var game = Auction();
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(BidLevel.Four, GumballId.Poker));
        for (var seat = 1; seat < 4; seat++) game.ExecuteCampaignAction(seat, new CampaignCommand.Pass());
        Assert.Equal("PokerReveal", game.Phase.ToString());
        var run = new CampaignRun(Phase: CampaignPhase.InMatch).WithCheckpoint(new(game.ExportCampaignMatchSnapshot()));
        var path = Path.Combine(Path.GetTempPath(), $"poker-save-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(CampaignStore.Save(run, path));
            var original = File.ReadAllText(path);
            Assert.Equal(CampaignLoadStatus.Loaded, CampaignStore.LoadSave(path).Status);
            // CardId(0) is legitimate; omitting its Value must not silently recreate it.
            foreach (var history in new[] { false, true })
            {
                var json = JsonNode.Parse(original)!;
                var live = json["Run"]!["LiveMatch"]!;
                var result = history ? live["HandHistory"]![0]!["PokerResult"]! : live["PokerResult"]!;
                var zero = result["Winners"]![0]!["CardIds"]!.AsArray().Single(id => id!["Value"]!.GetValue<int>() == 0)!;
                zero.AsObject().Remove("Value");
                File.WriteAllText(path, json.ToJsonString());
                Assert.Equal(CampaignLoadStatus.Corrupt, CampaignStore.LoadSave(path).Status,
                    "Missing Poker card identity must reject rather than become CardId(0).");
            }
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    }
}
