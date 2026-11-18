using System.Security.Cryptography;
using System.Text;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Server;

// Offline browser test setup only. This is linked solely into the executable test project.
static class CampaignBrowserFixtures
{
    public static void Write(string root, string token, string kind)
    {
        if (!WebCampaigns.ValidToken(token)) throw new ArgumentException("Invalid fixture token.");
        var run = kind switch
        {
            "shop" => Shop(), "shop-full-jokers" => Shop(fullJokers: true), "shop-use" => ArcadeReady(), "arcade-win" => CampaignArcade.PlayPlinko(ArcadeReady(), 4), "arcade-loss" => ArcadeReady(1), "arcade-ticket" => ArcadeReady(3), "arcade-legendary" => LegendaryReady(), "gambler" => Gambler(), "gambler-hand" => HumanTurn(gambler: true), "human-turn" => HumanTurn(), "match-end" => MatchEnd(),
            _ => throw new ArgumentException("Unknown campaign fixture.")
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var path = Path.Combine(Path.GetFullPath(root), hash, "campaign.json");
        if (!CampaignStore.Save(new CampaignSave(3, run) { Metadata = new("browser-fixture", 1) }, path)) throw new IOException("Could not write test fixture.");
    }
    private static CampaignRun Shop(ulong seed = 12, bool fullJokers = false)
    {
        var resources = CampaignSnapshot.Create(seed); resources.Wallets[0] = 100;
        JokerState[] jokers = fullJokers ? Enum.GetValues<JokerId>().Take(5).Select(id => new JokerState(id)).ToArray() : [new(JokerId.Alpha)];
        resources = resources with { Inventory = new(jokers, [TarotId.Chameleon, TarotId.DevilishDeal], Enum.GetValues<GumballId>()) { Sigils = [SigilId.Bear] } };
        return CampaignShop.Open(new CampaignRun(Match: 2, Money: 100, Phase: CampaignPhase.Shop) { Resources = resources });
    }
    private static CampaignRun ArcadeReady(int score = 5)
    {
        for (ulong seed = 1; seed < 1000; seed++)
        {
            var run = Shop(seed);
            run = run with { Resources = run.Resources with { Inventory = run.Resources.Inventory with
            { Tarot = [TarotId.Gambler, TarotId.TaxCollector, TarotId.Astronomer] } } };
            if (CampaignArcade.Available(run) && CampaignArcade.PlayPlinko(run, 4).Shop!.Arcade!.Pending!.Score == score) return run;
        }
        throw new InvalidOperationException("No Arcade fixture found.");
    }
    private static CampaignRun LegendaryReady()
    {
        for (ulong seed = 1; seed < 20000; seed++)
        {
            var run = Shop(seed);
            if (CampaignArcade.Available(run) && CampaignArcade.PlayPlinko(run, 4).Shop!.Arcade!.Pending!.PackCards?.Contains(CampaignArcade.LegendaryKey) is true) return run;
        }
        throw new InvalidOperationException("No Legendary fixture found.");
    }
    private static CampaignRun Gambler()
    {
        for (ulong seed = 1; seed < 100; seed++)
        {
            var run = Shop(seed);
            var offer = run.Shop!.Offers.Single(o => o.Category == FeatureCategory.Tarot);
            run = CampaignShop.PrintTickets(run, offer.Id, CampaignShop.TicketSpread);
            if (run.Shop!.Offers.Single(o => o.Id == offer.Id).Printed!.Contains(nameof(TarotId.Gambler))) return run;
        }
        throw new InvalidOperationException("No printed Gambler fixture found.");
    }
    private static CampaignRun HumanTurn(bool gambler = false)
    {
        var run = new CampaignRun { Resources = CampaignSnapshot.Create(45) };
        if (gambler) run = run with { Resources = run.Resources with { Inventory = new([], [TarotId.Gambler], []) } };
        var game = Game(run, 7); game.StartGame(3);
        return run.StartMatch().WithCheckpoint(new(game.ExportCampaignMatchSnapshot()));
    }
    private static GameEngine Game(CampaignRun run, int seed) => new(["You", "Opponent 1", "Partner", "Opponent 3"], seed, run.NextMatch(), new CampaignContext(run.Resources));
    private static CampaignRun MatchEnd()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var run = new CampaignRun { Resources = CampaignSnapshot.Create((ulong)seed) };
            var game = Game(run, seed); game.StartGame(3);
            for (var guard = 0; guard < 2000 && game.Phase != GamePhase.GameComplete; guard++)
            {
                if (game.Phase == GamePhase.HandComplete) { game.StartNextHand(); continue; }
                var seat = game.CampaignActor!.Value;
                var command = CampaignBotPolicy.Choose(game.CreateView(seat), seat);
                var before = game.ExportCampaignMatchSnapshot();
                game.ExecuteCampaignAction(seat, command);
                if (seat == 0 && command is CampaignCommand.Play && game.Phase == GamePhase.GameComplete && game.GameWinner == 0)
                    return run.StartMatch().WithCheckpoint(new(before));
            }
        }
        throw new InvalidOperationException("No terminal human fixture found.");
    }
}
