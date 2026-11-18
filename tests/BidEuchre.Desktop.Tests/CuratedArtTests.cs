using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Desktop;
using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;

internal static class CuratedArtTests
{
    public static void ContainsSigil(Control control, SigilId sigil)
    {
        // Playing cards keep the small square Sigil badge; card-shaped surfaces use the collector art.
        var badge = ((Image)((Grid)PixelArt.CardContent(Card.Parse("9H"), "curated-sigil-" + sigil.ToString().ToLowerInvariant())).Children[0]).Source;
        if (!control.GetLogicalDescendants().OfType<Image>().Any(image => image.Name == "CardSigil" && ReferenceEquals(image.Source, badge)))
            throw new Exception($"Card must carry its {sigil} Sigil artwork.");
    }

    public static void Run()
    {
        CollectorArt();
        var sheetDirectory = FindJokerSheetDirectory();
        using var sheet = JsonDocument.Parse(File.ReadAllText(Path.Combine(sheetDirectory, "jokers.json")));
        var approvedJokers = sheet.RootElement.GetProperty("frames");
        var jokers = FeatureCatalog.All.Where(feature => feature.Category == FeatureCategory.Joker).ToArray();
        if (approvedJokers.EnumerateObject().Count() != jokers.Length)
            throw new Exception("The approved Joker sheet must match the active catalogue.");
        // A missing curated route or square stretch must not silently replace or squash a supplied Joker.
        foreach (var joker in jokers)
        {
            var icon = PixelArt.CampaignIcon(joker, 120);
            if (icon.Stretch != Stretch.Uniform)
                throw new Exception($"{joker.Name} must fit its portrait artwork without stretching.");
            var resource = "curated-" + joker.Id.Replace(':', '-');
            var uri = new Uri($"avares://BidEuchre/Pixel/{resource}.png");
            if (!AssetLoader.Exists(uri)) throw new Exception($"{joker.Name} must embed its supplied Joker artwork.");
            using var stream = AssetLoader.Open(uri);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var embedded = bytes.ToArray();
            var approved = approvedJokers.GetProperty(joker.Id);
            var expectedHash = approved.GetProperty("sha256").GetString();
            var approvedPath = Path.Combine(sheetDirectory, approved.GetProperty("file").GetString()!);
            if (Convert.ToHexStringLower(SHA256.HashData(embedded)) != expectedHash
                || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(approvedPath))) != expectedHash)
                throw new Exception($"{joker.Name} must embed the exact approved uniform-border sprite, not its old portrait.");
            using var bitmap = SkiaSharp.SKBitmap.Decode(embedded);
            var expected = ((Image)((Grid)PixelArt.CardContent(Card.Parse("9H"), resource)).Children[0]).Source;
            if (bitmap is null || bitmap.Width != 280 || bitmap.Height != 392 || !ReferenceEquals(icon.Source, expected))
                throw new Exception($"{joker.Name} must use its supplied 5:7 portrait in all shared surfaces.");
            if (bitmap.GetPixel(4, 4).Alpha != 0 || bitmap.GetPixel(275, 387).Alpha != 0
                || bitmap.GetPixel(4, 100) != new SkiaSharp.SKColor(27, 23, 19, 255)
                || bitmap.GetPixel(6, 100) != new SkiaSharp.SKColor(244, 231, 205, 255))
                throw new Exception($"{joker.Name} must preserve the shared rounded outline, cream rim and transparent corners.");
        }
        // Catch a ticket falling back to old square artwork or being stretched in any shared surface.
        foreach (var tarot in FeatureCatalog.All.Where(feature => feature.Category == FeatureCategory.Tarot))
        {
            var resource = tarot.Key == nameof(TarotId.Chameleon) ? "curated-tarot-system-error" : "curated-" + tarot.Id.Replace(':', '-');
            var uri = new Uri($"avares://BidEuchre/Pixel/{resource}.png");
            if (!AssetLoader.Exists(uri)) throw new Exception($"{tarot.Name} must have its supplied arcade ticket embedded.");
            var icon = PixelArt.CampaignIcon(tarot, 120);
            if (PixelArt.CampaignImageName(tarot) != resource) throw new Exception($"{tarot.Name} must use its matching ticket art.");
            // Avalonia's lightweight headless backend substitutes 1x1 bitmaps. Inspect the real embedded PNG.
            using var stream = AssetLoader.Open(uri);
            using var bitmap = SkiaSharp.SKBitmap.Decode(stream);
            if (bitmap is null || bitmap.Width != 256 || bitmap.Height != 384 || icon.Stretch != Stretch.Uniform)
                throw new Exception($"{tarot.Name} must preserve the ticket's 2:3 proportions in the shop, collection and inventory.");
        }
        var face = Card.Parse("9H");
        foreach (var sigil in new[] { SigilId.Fish, SigilId.Vulture, SigilId.Frog, SigilId.Spider, SigilId.Eagle, SigilId.Duck, SigilId.Bear, SigilId.Chameleon })
        {
            var definition = FeatureCatalog.Get(FeatureCategory.Sigil, sigil.ToString());
            var shop = PixelArt.CampaignIcon(definition, 120);
            if (!ReferenceEquals(shop.Source, PixelArt.CollectorImage(sigil.ToString())) || shop.Stretch != Stretch.Uniform)
                throw new Exception($"Inventory, collection and menus must show the {sigil} collector card without stretching.");
            var content = (Grid)PixelArt.CardContent(face, EnhancementId.Golden, sigil);
            ContainsSigil(content, sigil);
            if (((Image)content.Children[1]).Effect is not DropShadowEffect)
                throw new Exception("Curated darker substrates need a contrasting halo behind the original face sprite.");
        }
        foreach (var gumball in new[] { GumballId.HighLow, GumballId.Chaos, GumballId.Alone, GumballId.Lowballer,
            GumballId.Poker, GumballId.Duelist, GumballId.Telepath, GumballId.PartnersBest })
        {
            var definition = FeatureCatalog.Get(FeatureCategory.Gumball, gumball.ToString());
            var expected = ((Image)((Grid)PixelArt.CardContent(face, "curated-" + definition.Id.Replace(':', '-'))).Children[0]).Source;
            if (!ReferenceEquals(PixelArt.Gumball(gumball, 64)?.Source, expected) || !ReferenceEquals(PixelArt.CampaignIcon(definition).Source, expected))
                throw new Exception($"Machine, inventory and shop must share curated {gumball} art.");
        }
        ContainsSigil(PixelArt.CardContent(face, null, SigilId.Bear), SigilId.Bear);
        var plain = (Grid)PixelArt.CardContent(face);
        if (plain.Children.Count != 2 || ((Image)plain.Children[1]).Effect is not null)
            throw new Exception("Ordinary unenhanced cards must retain their two art layers.");
        var motion = new PixelMotion(); motion.SetEnabled(false);
        EnhancementSprites(motion);
        BossArenas(motion);
        TicketCollection(motion);
        DoubleDownCollection(motion);
        ShopMachines(motion);
        var picker = CampaignDeckPicker.Build([new(new(0), face, EnhancementId.Glass, SigilId.Fish)], 0, _ => { }, () => { }, motion, "Curated picker");
        ContainsSigil(picker, SigilId.Fish);
        Console.WriteLine("PASS  Supplied Jokers, tickets, shop machines, Gumballs, Sigils and enhanced card badges use imported artwork");
    }

    private static void EnhancementSprites(PixelMotion motion)
    {
        var sheetDirectory = Path.Combine(Path.GetDirectoryName(FindJokerSheetDirectory())!, "enhancements");
        using var sheet = JsonDocument.Parse(File.ReadAllText(Path.Combine(sheetDirectory, "enhancements.json")));
        var approved = sheet.RootElement.GetProperty("frames");
        var enhancements = FeatureCatalog.All.Where(feature => feature.Category == FeatureCategory.Enhancement).ToArray();
        if (approved.EnumerateObject().Count() != enhancements.Length) throw new Exception("The approved enhancement sheet must match the active catalogue.");
        foreach (var feature in enhancements)
        {
            var resource = "curated-" + feature.Id.Replace(':', '-');
            using var stream = AssetLoader.Open(new Uri($"avares://BidEuchre/Pixel/{resource}.png"));
            using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            var frame = approved.GetProperty(feature.Id);
            var expectedHash = frame.GetProperty("sha256").GetString();
            if (Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())) != expectedHash
                || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(sheetDirectory, frame.GetProperty("file").GetString()!)))) != expectedHash)
                throw new Exception($"{feature.Name} must embed the exact approved transparent enhancement sprite.");
            using var texture = SkiaSharp.SKBitmap.Decode(bytes.ToArray());
            if (texture is null || texture.Width != 124 || texture.Height != 176 || texture.GetPixel(0, 0).Alpha != 0)
                throw new Exception($"{feature.Name} must retain its native size and transparent exterior.");
            var content = (Grid)PixelArt.CardContent(Card.Parse("9H"), Enum.Parse<EnhancementId>(feature.Key), SigilId.Bear);
            if (content.Background is ISolidColorBrush brush && brush.Color.A != 0)
                throw new Exception($"{feature.Name} artwork must not be flattened over a white rectangle.");
            if (!ReferenceEquals(((Image)content.Children[0]).Source, PixelArt.CampaignIcon(feature).Source))
                throw new Exception($"{feature.Name} must use the imported artwork on cards and catalogue icons.");
            ContainsSigil(content, SigilId.Bear);
        }
        if (((Grid)PixelArt.CardContent(Card.Parse("9H"))).Background is not ISolidColorBrush { Color: var paper } || paper != Colors.White)
            throw new Exception("Ordinary cards must keep their white paper.");

        var owner = new MainWindow();
        var rows = new StackPanel { Spacing = 16, Margin = new Thickness(16) };
        var samples = new List<(Grid Content, Color Backdrop, EnhancementId Enhancement)>();
        var view = new GameEngine(["You", "West", "Partner", "East"], 1).CreateView(0);
        foreach (var group in enhancements.Chunk(4))
        {
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16 };
            foreach (var feature in group)
            {
                var enhancement = Enum.Parse<EnhancementId>(feature.Key);
                var cards = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
                foreach (var dark in new[] { true, false })
                {
                    var face = dark ? Card.Parse("9H") : Card.Parse("KS");
                    var backdrop = Color.Parse(dark ? "#17384A" : "#F1DFC7");
                    Control card;
                    Grid content;
                    if (dark)
                    {
                        var button = (Button)typeof(MainWindow).GetMethod("CardButton", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .Invoke(owner, [face, true, view, null, enhancement, SigilId.Bear, null])!;
                        if (button.Background is ISolidColorBrush brush && brush.Color.A != 0)
                            throw new Exception("Enhanced hand cards must reveal the table through their exterior alpha.");
                        card = button; content = (Grid)button.Content!;
                    }
                    else
                    {
                        var displayed = (Border)typeof(MainWindow).GetMethod("CardFace", BindingFlags.NonPublic | BindingFlags.Static)!
                            .Invoke(null, [face, enhancement, SigilId.Fish])!;
                        if (displayed.Background is ISolidColorBrush brush && brush.Color.A != 0 || displayed.ClipToBounds)
                            throw new Exception("Enhanced trick cards must preserve their own alpha silhouette without extra corner clipping.");
                        card = displayed; content = (Grid)displayed.Child!;
                    }
                    samples.Add((content, backdrop, enhancement));
                    cards.Children.Add(new Border { Background = new SolidColorBrush(backdrop), Padding = new Thickness(8), Child = card });
                }
                row.Children.Add(new StackPanel { Spacing = 8, Children =
                {
                    new TextBlock { Text = feature.Name, Foreground = Brushes.White, FontSize = 18 }, cards
                } });
            }
            rows.Children.Add(row);
        }
        var window = new Window { Width = 1240, Height = 515, Background = Brush.Parse("#101725"), Content = rows };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Capture(window, "enhancements-contrasting-surfaces");
            if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not null)
            {
                using var rendered = window.CaptureRenderedFrame()!;
                using var bytes = new MemoryStream(); rendered.Save(bytes, new PngBitmapEncoderOptions());
                using var pixels = SkiaSharp.SKBitmap.Decode(bytes.ToArray());
                // The legal/selection outline can cross the PNG's corner cutouts. Glass's
                // interior gives an unambiguous rendered-alpha check, clear of that outline.
                foreach (var (content, backdrop, _) in samples.Where(sample => sample.Enhancement == EnhancementId.Glass))
                {
                    var exposed = 0;
                    var count = 0;
                    for (var y = 20; y < content.Bounds.Height - 20; y++)
                    for (var x = 15; x < content.Bounds.Width - 15; x++)
                    {
                        var point = content.TranslatePoint(new Point(x + .5, y + .5), window)!.Value;
                        var actual = pixels.GetPixel((int)point.X, (int)point.Y);
                        if (Math.Abs(actual.Red - backdrop.R) <= 3 && Math.Abs(actual.Green - backdrop.G) <= 3 && Math.Abs(actual.Blue - backdrop.B) <= 3) exposed++;
                        count++;
                    }
                    if (exposed < count / 5)
                        throw new Exception("Glass must reveal its actual contrasting surface through its transparent interior, not a hidden white backing.");
                }
            }
            window.Content = CollectionPage.Build(() => { }, motion);
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CollectionEnhancements")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Width = 1280; window.Height = 900; Dispatcher.UIThread.RunJobs();
            Capture(window, "enhancement-collection");
            ((PixelMotion)typeof(MainWindow).GetField("_motion", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!).SetEnabled(false);
            var handTypes = new[] { EnhancementId.Lucky, EnhancementId.Golden, EnhancementId.Glass, EnhancementId.Cursed, EnhancementId.Stone, EnhancementId.Silver };
            var hand = handTypes.Select((enhancement, index) => new CampaignCardView(new(index),
                new Card(index % 2 == 0 ? Suit.Hearts : Suit.Spades, (Rank)(9 + index)), enhancement, SigilId.Bear)).ToArray();
            var players = Enumerable.Range(0, 4).Select(seat => new CampaignPlayerView(seat, seat == 0 ? hand.Length : 0,
                seat == 0 ? hand : [], false)).ToArray();
            var played = new[] { EnhancementId.Glass, EnhancementId.Wild, EnhancementId.Golden, EnhancementId.Bouncy }
                .Select((enhancement, seat) => new CampaignTrickPlay(seat, new(new(10 + seat), new Card((Suit)seat, Rank.King), enhancement, SigilId.Fish), (Suit)seat)).ToArray();
            var campaign = new CampaignGameView(0, 0, 0, players, hand.Select(card => (CampaignCommand)new CampaignCommand.Play(card.Id)).ToArray(),
                played, [], CampaignInventory.Empty, 0, 1, 20, null);
            var table = view with { Phase = GamePhase.Playing, CurrentSeat = 0, Campaign = campaign,
                CurrentTrick = played.Select(play => new CardPlay(play.Seat, play.Card.Face)).ToArray() };
            Control Surface(string method, params object[] arguments) => (Control)typeof(MainWindow)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, arguments)!;
            window.Content = new StackPanel { Spacing = 24, Margin = new Thickness(24), Children =
            {
                new TextBlock { Text = "Enhanced cards in the hand and current trick", Foreground = Brushes.White, FontSize = 24 },
                Surface("TrickPanel", table),
                Surface("PlayerPanel", view.Players[0] with { CardCount = hand.Length }, table)
            } };
            Dispatcher.UIThread.RunJobs();
            Capture(window, "enhancements-hand-and-trick");
        }
        finally { window.Close(); owner.Close(); }
        Console.WriteLine("PASS  All eight enhancements embed approved transparent sprites and preserve card ink, sigils and alpha on light/dark surfaces");
    }

    private static string FindJokerSheetDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var sheetDirectory = Path.Combine(directory.FullName, "assets", "sheets", "jokers");
            if (File.Exists(Path.Combine(sheetDirectory, "jokers.json"))) return sheetDirectory;
        }
        throw new Exception("The approved Joker sheet must be available in this test checkout.");
    }

    private static void BossArenas(PixelMotion motion)
    {
        var themes = new[] { "club", "diamond", "heart", "spade", "cursed", "club" };
        for (var index = 0; index < themes.Length; index++)
        {
            var run = new CampaignRun(Match: (index + 1) * 3, Phase: CampaignPhase.InMatch);
            var scene = PixelArt.TableScene(motion, run);
            var expected = ((Image)((Grid)PixelArt.CardContent(Card.Parse("9H"), "boss-arena-" + themes[index])).Children[0]).Source;
            if (scene is not Image arena || !ReferenceEquals(arena.Source, expected) || arena.Stretch != Stretch.UniformToFill)
                throw new Exception($"Boss Ante {index + 1} must show its complete {themes[index]} arena without old suit overlays.");
            foreach (var ordinary in new[] { run with { Match = run.Match - 1 }, run with { Phase = CampaignPhase.Shop }, run with { Phase = CampaignPhase.Intro } })
                if (PixelArt.TableScene(motion, ordinary) is Image)
                    throw new Exception("Boss artwork must not persist in ordinary matches, the shop or ante preview.");
        }
        if (PixelArt.TableScene(motion) is Image) throw new Exception("Free play and menus must retain the default scene.");
    }

    private static void TicketCollection(PixelMotion motion)
    {
        var window = new Window { Width = 1280, Height = 900, Content = CollectionPage.Build(() => { }, motion) };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Capture(window, "curated-joker-collection");
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CollectionTarot")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var sources = FeatureCatalog.All.Where(feature => feature.Category == FeatureCategory.Tarot)
                .Select(feature => PixelArt.CampaignIcon(feature).Source).ToHashSet();
            var tickets = window.GetVisualDescendants().OfType<Image>().Where(image => sources.Contains(image.Source)).ToArray();
            if (tickets.Length != 9 || tickets.Any(image => image.Bounds.Height <= 0 || image.Stretch != Stretch.Uniform))
                throw new Exception("The collection must display all nine arcade tickets without squashing them.");
            Capture(window, "arcade-ticket-collection");
            window.Content = PixelArt.TableScene(motion, new CampaignRun(Match: 15, Phase: CampaignPhase.InMatch));
            Dispatcher.UIThread.RunJobs();
            Capture(window, "cursed-boss-arena");
        }
        finally { window.Close(); }
    }

    private static void DoubleDownCollection(PixelMotion motion)
    {
        var window = new Window { Width = 1120, Height = 800, Content = CollectionPage.Build(() => { }, motion) };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var tile = window.GetVisualDescendants().OfType<Border>().Single(entry => Equals(entry.Tag, "joker:double-down"));
            tile.BringIntoView(); Dispatcher.UIThread.RunJobs();
            var expected = PixelArt.CampaignIcon(FeatureCatalog.Get(FeatureCategory.Joker, "DoubleDown")).Source;
            if (!tile.GetVisualDescendants().OfType<Image>().Any(image => ReferenceEquals(image.Source, expected) && image.Bounds.Height > 0)
                || !tile.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Double Down"))
                throw new Exception("Double Down must show its approved Boomerang portrait under its unchanged gameplay name.");
            Capture(window, "double-down-collection");
        }
        finally { window.Close(); }
    }

    private static void ShopMachines(PixelMotion motion)
    {
        var run = new CampaignRun(Money: 30, Phase: CampaignPhase.Shop)
        {
            Shop = new(0, [new("j1", FeatureCategory.Joker, "DoubleDown", 4), new("j2", FeatureCategory.Joker, "HighRoller", 4),
                new("s", FeatureCategory.Sigil, "Fish", 3), new("t", FeatureCategory.Tarot, "Prospector", 3), new("g", FeatureCategory.Gumball, "", 3)])
        };
        string? visited = null;
        var actions = new ShopActions(_ => { }, _ => { }, _ => { }, (_, _) => { }, () => { }, () => { })
        { Pull = _ => null, Refresh = () => { }, Reroll = () => { }, Teller = id => visited = id };
        var window = new Window { Width = 1280, Height = 900, Content = CampaignShopScreen.Build(run, actions, motion) };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            foreach (var (buttonName, resource) in new[] { ("GumballMachine", "machine-gumball"), ("FortuneTeller", "machine-fortune-teller-ticket-dispenser") })
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == buttonName);
                var expected = ((Image)((Grid)PixelArt.CardContent(Card.Parse("9H"), resource)).Children[0]).Source;
                var art = button.GetVisualDescendants().OfType<Image>().SingleOrDefault(image => ReferenceEquals(image.Source, expected));
                if (art is null || art.Stretch != Stretch.Uniform || art.Bounds.Width <= 0 || art.Bounds.Height <= 0)
                    throw new Exception($"{buttonName} must show the complete supplied machine within its existing shop offer.");
            }
            var teller = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "FortuneTeller");
            // Her tickets are blind: the card must not reveal which ticket will come out.
            var ticketSource = PixelArt.CampaignIcon(FeatureCatalog.Get(FeatureCategory.Tarot, "Prospector")).Source;
            if (teller.GetVisualDescendants().OfType<Image>().Any(image => ReferenceEquals(image.Source, ticketSource)))
                throw new Exception("The Fortune Teller's card must not show a ticket before it is printed.");
            Capture(window, "curated-shop-machines");
            teller.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            if (visited != "t") throw new Exception("The Fortune Teller must open her menu for the existing offer, preserving its stable ID.");
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BIDEUCHRE_RENDER_TESTS") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        if (bitmap is null) throw new Exception("The imported artwork must render successfully.");
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        bitmap.Save(stream, new PngBitmapEncoderOptions());
    }

    // A pack card must show its supplied 2:3 collector artwork, embedded exactly as the manifest records it.
    private static void CollectorArt()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "BidEuchre.slnx"))) root = Path.GetDirectoryName(root)!;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "assets/pixel/curated-art.json")));
        var entries = manifest.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        foreach (var key in new[] { "Bear", "Eagle", "Fish", "Spider", "Frog", "Duck", "Vulture", "Chameleon", "Legendary", "Pack" })
        {
            var resource = "curated-collector-" + key.ToLowerInvariant();
            var entry = entries.SingleOrDefault(e => e.GetProperty("resource").GetString() == resource);
            if (entry.ValueKind != JsonValueKind.Object || entry.GetProperty("category").GetString() != "collector")
                throw new Exception($"{key} must have a collector entry in the curated art manifest.");
            var uri = new Uri($"avares://BidEuchre/Pixel/{resource}.png");
            if (!AssetLoader.Exists(uri)) throw new Exception($"{key} collector art must be embedded.");
            using var stream = AssetLoader.Open(uri);
            using var bitmap = SkiaSharp.SKBitmap.Decode(stream);
            if (bitmap is null || bitmap.Width != 256 || bitmap.Height != 384)
                throw new Exception($"{key} collector art must be 256x384.");
            if (PixelArt.CollectorImage(key) is null) throw new Exception($"{key} collector art must load.");
        }
        Console.WriteLine("PASS  All ten collector cards embed their supplied 2:3 artwork");
    }
}
