using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;

internal static class ArtworkProfile
{
    public static void Run(string screen, bool preload)
    {
        var timer = Stopwatch.StartNew();
        var window = new MainWindow();
        try
        {
            void Present()
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                if (frame is null) throw new Exception("Run artwork profiling with BIDEUCHRE_RENDER_TESTS set.");
            }
            void Measure(string stage, double ms, (int Count, long Bytes, double DecodeMilliseconds) before)
            {
                var after = PixelArt.ArtworkMetrics;
                Console.WriteLine($"{stage}: {ms:F1} ms, new bitmaps {after.Count - before.Count}, " +
                    $"decoded {(after.Bytes - before.Bytes) / 1048576d:F2} MiB, decode {after.DecodeMilliseconds - before.DecodeMilliseconds:F1} ms, total {after.Bytes / 1048576d:F2} MiB");
            }

            window.Show();
            Present();
            Measure("startup", timer.Elapsed.TotalMilliseconds, default);
            var motion = new PixelMotion();
            motion.SetEnabled(false);
            var run = new CampaignRun(Match: 3, Phase: CampaignPhase.InMatch);
            var shop = CampaignShop.Open(new CampaignRun(Match: 2, Money: 10, Phase: CampaignPhase.Shop)
                { Resources = CampaignSnapshot.Create(42) });
            var actions = new ShopActions(_ => { }, _ => { }, _ => { }, (_, _) => { }, () => { }, () => { })
                { Pull = _ => null, Refresh = () => { } };
            if (preload)
            {
                var cold = PixelArt.ArtworkMetrics;
                var artwork = screen switch
                {
                    "collection" => ScreenArtwork.Collection(FeatureCategory.Joker),
                    "shop" => ScreenArtwork.Shop(shop),
                    "boss" => [PixelArt.BossArenaName(run)!],
                    _ => throw new ArgumentException("Expected collection, shop or boss.")
                };
                timer.Restart();
                // Two overlapping requests also verify that the shared cache decodes each bitmap once.
                var names = artwork.Distinct().ToArray();
                Task.WhenAll(PixelArt.PreloadAsync(names), PixelArt.PreloadAsync(names)).GetAwaiter().GetResult();
                Measure("preload", timer.Elapsed.TotalMilliseconds, cold);
                if (names.Any(name => !PixelArt.IsArtworkReady(name)))
                    throw new Exception("The entire destination plan should already be decoded.");
                if (screen == "boss" && PixelArt.ArtworkMetrics.Count != cold.Count + 1)
                    throw new Exception("An arena preload must decode only the requested arena, once.");
            }
            var before = PixelArt.ArtworkMetrics;
            timer.Restart();
            window.Content = screen switch
            {
                "collection" => CollectionPage.Build(() => { }, motion),
                "shop" => CampaignShopScreen.Build(shop, actions, motion),
                "boss" => PixelArt.TableScene(motion, run),
                _ => throw new ArgumentException("Expected collection, shop or boss.")
            };
            Present();
            Measure(screen, timer.Elapsed.TotalMilliseconds, before);
            if (preload && PixelArt.ArtworkMetrics.Count != before.Count)
                throw new Exception("Presenting the preloaded screen unexpectedly decoded more images.");
        }
        finally { window.Close(); }
    }
}
