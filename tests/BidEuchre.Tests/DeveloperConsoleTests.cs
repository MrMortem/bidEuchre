using BidEuchre.App;
using BidEuchre.Core;

internal static class DeveloperConsoleTests
{
    // Applies changes straight to an in-memory run, as the desktop does between matches.
    private sealed class FakeHost(CampaignRun run) : IDeveloperHost
    {
        public CampaignRun? Run { get; private set; } = run;
        public bool InMatch { get; set; }
        public IReadOnlyList<int>? Scores { get; set; }
        public IReadOnlyList<CardId>? HumanHand { get; set; }
        public (int, int)? SetScores;
        public bool? Ended;

        public Task UpdateResourcesAsync(Func<CampaignSnapshot, CampaignSnapshot> update)
        {
            var resources = update(Run!.Resources);
            if (!resources.IsValid()) throw new GameRuleException("invalid");
            Run = Run with { Resources = resources, Money = resources.Wallets[0] };
            return Task.CompletedTask;
        }

        public Task UpdateRunAsync(Func<CampaignRun, CampaignRun> update) { Run = update(Run!); return Task.CompletedTask; }
        public Task SetScoresAsync(int us, int them) { SetScores = (us, them); return Task.CompletedTask; }
        public Task EndMatchAsync(bool won) { Ended = won; return Task.CompletedTask; }
    }

    private static (DeveloperConsole Console, FakeHost Host) Create(CampaignPhase phase = CampaignPhase.Shop)
    {
        var host = new FakeHost(new CampaignRun(Money: 10, Phase: phase) { Resources = CampaignSnapshot.Create(7) with { Wallets = [10, 0, 0, 0] } });
        return (new DeveloperConsole(host), host);
    }

    private static bool HasError(IReadOnlyList<ConsoleLine> lines) => lines.Any(l => l.Tone == ConsoleTone.Error);

    public static async Task MoneyAndLives()
    {
        var (console, host) = Create();
        await console.ExecuteAsync("money 50");
        Assert.Equal(50, host.Run!.Money);
        await console.ExecuteAsync("money +5; money -100");
        Assert.Equal(0, host.Run!.Money);
        await console.ExecuteAsync("lives 7");
        Assert.Equal(7, host.Run!.Lives);
        Assert.True(HasError(await console.ExecuteAsync("lives 0")));
        Assert.Equal(7, host.Run!.Lives);
    }

    public static async Task JokersByFuzzyName()
    {
        var (console, host) = Create();
        await console.ExecuteAsync("joker add king midas");
        await console.ExecuteAsync("joker add slick");
        Assert.True(host.Run!.Resources.Inventory.Has(JokerId.KingMidas));
        Assert.True(host.Run!.Resources.Inventory.Has(JokerId.SlickWilly));
        Assert.True(HasError(await console.ExecuteAsync("joker add kingmidas")), "duplicates are refused");
        await console.ExecuteAsync("joker value midas 3");
        Assert.Equal(3, host.Run!.Resources.Inventory.Jokers.Single(j => j.Id == JokerId.KingMidas).Value);
        await console.ExecuteAsync("joker remove king-midas");
        Assert.False(host.Run!.Resources.Inventory.Has(JokerId.KingMidas));
        foreach (var name in new[] { "alpha", "bully", "medusa", "cheater" }) await console.ExecuteAsync($"joker add {name}");
        Assert.True(HasError(await console.ExecuteAsync("joker add one up")), "only five slots");
        await console.ExecuteAsync("joker clear");
        Assert.Equal(0, host.Run!.Resources.Inventory.Jokers.Length);
    }

    public static async Task TarotSigilsAndBids()
    {
        var (console, host) = Create();
        await console.ExecuteAsync("tarot add glassblower 2");
        await console.ExecuteAsync("sigil add coin");
        Assert.Equal(3, host.Run!.Resources.Inventory.ItemCount);
        Assert.True(HasError(await console.ExecuteAsync("tarot add prospector 2")), "four item slots");
        await console.ExecuteAsync("tarot remove #1");
        Assert.Equal(1, host.Run!.Resources.Inventory.Tarot.Length);
        await console.ExecuteAsync("sigil remove coin");
        Assert.Equal(0, host.Run!.Resources.Inventory.Sigils.Length);
        await console.ExecuteAsync("gumball unlock all");
        Assert.Equal(CampaignInventory.GumballCount, host.Run!.Resources.Inventory.Unlocks.Length);
        await console.ExecuteAsync("gumball lock chaos");
        Assert.False(host.Run!.Resources.Inventory.Unlocks.Contains(GumballId.Chaos));
        Assert.True(HasError(await console.ExecuteAsync("tarot add chameleon wizard")));
    }

    public static async Task DeckCards()
    {
        var (console, host) = Create();
        await console.ExecuteAsync("card 0 enhance glass");
        Assert.True(host.Run!.Resources.Deck[0].Enhancement == EnhancementId.Glass);
        await console.ExecuteAsync("card AS sigil frog");
        Assert.True(host.Run!.Resources.Deck.Single(c => c.Face == new Card(Suit.Spades, Rank.Ace)).Sigil == SigilId.Frog);
        await console.ExecuteAsync("card 0 suit hearts; card 0 rank K");
        Assert.Equal(new Card(Suit.Hearts, Rank.King), host.Run!.Resources.Deck[0].Face);
        await console.ExecuteAsync("card all enhance stone");
        Assert.True(host.Run!.Resources.Deck.All(c => c.Enhancement == EnhancementId.Stone));
        Assert.True(HasError(await console.ExecuteAsync("card hand enhance stone")), "no hand outside a match");
        await console.ExecuteAsync("deck reset");
        Assert.True(host.Run!.Resources.Deck.SequenceEqual(CampaignCards.CreateDeck()));
    }

    public static async Task RunFlowAndMatchCommands()
    {
        var (console, host) = Create();
        await console.ExecuteAsync("ante 3");
        Assert.Equal(7, host.Run!.Match);
        host.InMatch = true;
        Assert.True(HasError(await console.ExecuteAsync("match 2")), "no jumping mid-match");
        await console.ExecuteAsync("score 20 3");
        Assert.True(host.SetScores == (20, 3));
        await console.ExecuteAsync("lose");
        Assert.True(host.Ended == false);
        Assert.True(HasError(await console.ExecuteAsync("frobnicate")));
    }

    public static void EngineEndsMatch()
    {
        var game = new GameEngine(["a", "b", "c", "d"], 1, new MatchOptions(24), new CampaignContext(CampaignSnapshot.Create(3)));
        game.StartGame(0);
        game.DeveloperReplaceResources(s => s with { Wallets = [42, 0, 0, 0] });
        Assert.Equal(42, game.ExportCampaignSnapshot().Wallets[0]);
        game.DeveloperSetScores(10, 12);
        game.DeveloperEndMatch(0);
        Assert.True(game.Phase == GamePhase.GameComplete);
        Assert.True(game.GameWinner == 0);
        Assert.True(game.Scores[0] > game.Scores[1]);
        Assert.Throws<GameRuleException>(() => game.DeveloperReplaceResources(s => s with { Wallets = [-1, 0, 0, 0] }));
    }
}
