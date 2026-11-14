using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Desktop;
using System.Text.Json.Nodes;

internal static class CampaignRecoveryUiTests
{
    public static void Run()
    {
        foreach (var badVersion in new[] { false, true })
        {
            var folder = Path.Combine(Path.GetTempPath(), $"campaign-continue-{Guid.NewGuid():N}");
            var path = Path.Combine(folder, "campaign.json");
            var window = new Window { Width = 1280, Height = 900 };
            try
            {
                Directory.CreateDirectory(folder);
                var run = CampaignShop.Open(new CampaignRun(Match: 2, Money: 20, Phase: CampaignPhase.Shop));
                if (!CampaignStore.Save(run, path)) throw new Exception("Could not create disposable save.");
                var malformed = JsonNode.Parse(File.ReadAllText(path))!;
                if (badVersion) malformed["Version"] = "bogus";
                else malformed["Run"]!["Shop"]!["Offers"] = null;
                File.WriteAllText(path, malformed.ToJsonString());
                var motion = new PixelMotion(); motion.SetEnabled(false);
                var flow = new CampaignFlow(path, screen => window.Content = screen, _ => Task.CompletedTask, () => { }, motion);
                flow.Open(); window.Show(); Dispatcher.UIThread.RunJobs();
                if (flow.Run is not { Match: 1, Money: 0, Phase: CampaignPhase.Intro } ||
                    !window.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "CampaignStartMatch"))
                    throw new Exception("An invalid save must recover to a playable fresh run without crashing Continue.");
                Console.WriteLine($"PASS  Campaign {(badVersion ? "invalid version" : "malformed shop")} opens a playable recovery screen");
            }
            finally { window.Close(); Directory.Delete(folder, true); }
        }
    }
}
