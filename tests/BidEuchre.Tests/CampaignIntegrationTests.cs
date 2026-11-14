using BidEuchre.Core;
using BidEuchre.App;
internal static class CampaignIntegrationTests
{
    public static void SeededRuns()
    {
        var matches = 0; var purchases = 0; var negativeEndings = 0; var hands = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var random = new CampaignRandom(seed);
            var run = new CampaignRun() { Resources = CampaignSnapshot.Create(seed) };
            while (!run.IsOver && run.Match <= 15)
            {
                run = run.StartMatch();
                var game = new GameEngine(["You", "W", "P", "E"], (int)seed, run.NextMatch(), new CampaignContext(run.Resources)); game.StartGame();
                var guard = 0;
                while (game.Phase != GamePhase.GameComplete && guard++ < 1500)
                {
                    if (game.Phase == GamePhase.HandComplete) { game.StartNextHand(); continue; }
                    var seat = game.CampaignActor!.Value; var view = game.CreateCampaignView(seat);
                    if (seat == 0 && game.Phase == GamePhase.Bidding && view.PendingChoice is null && view.Inventory.Tarot.Length > 0)
                    {
                        var tarot = view.Inventory.Tarot[0]; var count = tarot == TarotId.DevilishDeal ? 3 : tarot is TarotId.Gambler or TarotId.Astronomer or TarotId.TaxCollector ? 0 : 1;
                        try { game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, view.Players[0].VisibleCards.Take(count).Select(c => c.Id).ToArray())); continue; }
                        catch (GameRuleException) { } // exhausted Astronomer or constrained mutation is intentionally unavailable
                    }
                    game.ExecuteCampaignAction(seat, CampaignBotPolicy.Choose(game.CreateView(seat), seat));
                }
                Assert.True(guard < 1500, $"Run seed {seed}, match {run.Match} stalled");
                Assert.Equal(24, game.ExportCampaignSnapshot().Deck.Select(c => c.Id).Distinct().Count());
                var snapshot = game.ExportCampaignSnapshot(); run = run with { Resources = snapshot, Money = snapshot.Wallets[0] };
                (run, _) = run.ApplyResult(game.GameWinner == 0, game.Scores[0], game.Scores[1]);
                matches++; hands += game.HandNumber; if (game.Scores.Min() <= -game.CreateView(0).TargetScore) negativeEndings++;
                if (run.IsOver) break;
                if (run.Phase == CampaignPhase.Victory) run = run.AcknowledgeVictory();
                run = CampaignShop.Open(run);
                foreach (var offer in run.Shop!.Offers)
                {
                    if (run.Money < offer.Price) continue;
                    try { run = CampaignShop.Buy(run, offer.Id, offer.Category == FeatureCategory.Sigil ? new CardId(random.NextInt(24)) : null); purchases++; }
                    catch (GameRuleException) { } // full slots are a legal blocked purchase
                }
                run = run.LeaveShop();
            }
        }
        Assert.True(matches >= 12); Assert.True(purchases > 0);
        Console.WriteLine($"Campaign seeded sample: 12 runs, {matches} matches, {hands} hands, {purchases} purchases, {negativeEndings} negative-threshold endings.");
    }
    public static void EveryTarotConsumesOnce()
    {
        foreach (var tarot in Enum.GetValues<TarotId>())
        {
            var s = CampaignSnapshot.Create(8) with { Inventory = new([], [tarot], []), Wallets = [25, 0, 0, 0] };
            var game = new GameEngine(["You", "W", "P", "E"], 8, campaign: new CampaignContext(s)); game.StartGame(3);
            var view = game.CreateCampaignView(0);
            var count = tarot == TarotId.DevilishDeal ? 3 : tarot is TarotId.Gambler or TarotId.Astronomer or TarotId.TaxCollector ? 0 : 1;
            var targetSuit = tarot switch { TarotId.LovePotion => Suit.Hearts, TarotId.Nightshade => Suit.Spades, TarotId.CloverTonic => Suit.Clubs, TarotId.CrystalElixir => Suit.Diamonds, _ => (Suit?)null };
            var candidates = targetSuit is { } suit ? view.Players[0].VisibleCards.Where(c => c.Face.Suit != suit) : view.Players[0].VisibleCards;
            game.ExecuteCampaignAction(0, new CampaignCommand.UseTarot(0, candidates.Take(count).Select(c => c.Id).ToArray()));
            var result = game.ExportCampaignSnapshot(); Assert.Equal(0, result.Inventory.Tarot.Length);
            if (tarot == TarotId.TaxCollector) Assert.Equal(45, result.Wallets[0]);
            else if (tarot == TarotId.Astronomer) Assert.Equal(1, result.Inventory.Unlocks.Length);
            else Assert.True(result.Deck.Where((c, i) => c != s.Deck[i]).Any(), $"Tarot {tarot} made no mutation");
        }
    }
}
