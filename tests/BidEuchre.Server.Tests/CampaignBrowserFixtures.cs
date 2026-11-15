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
            "shop" => Shop(), "human-turn" => HumanTurn(), "match-end" => MatchEnd(),
            _ => throw new ArgumentException("Unknown campaign fixture.")
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var path = Path.Combine(Path.GetFullPath(root), hash, "campaign.json");
        if (!CampaignStore.Save(new CampaignSave(3, run) { Metadata = new("browser-fixture", 1) }, path)) throw new IOException("Could not write test fixture.");
    }
    private static CampaignRun Shop()
    {
        var resources = CampaignSnapshot.Create(12); resources.Wallets[0] = 100;
        resources = resources with { Inventory = new([new(JokerId.Alpha)], [TarotId.Chameleon, TarotId.DevilishDeal], Enum.GetValues<GumballId>()) { Sigils = [SigilId.Bear] } };
        return CampaignShop.Open(new CampaignRun(Match: 2, Money: 100, Phase: CampaignPhase.Shop) { Resources = resources });
    }
    private static CampaignRun HumanTurn()
    {
        var run = new CampaignRun { Resources = CampaignSnapshot.Create(45) };
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
