using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.App;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;
namespace BidEuchre.Desktop;

/// <summary>What the Fortune Teller's menu can ask of the run.</summary>
/// <param name="Print">Pays for 1 or 3 blind tickets and saves the run, without redrawing. Null when refused.</param>
/// <param name="Keep">Keeps the chosen ticket in a card slot and returns to the shop.</param>
/// <param name="Use">Plays the newly printed ticket at once, using its six offered cards when it changes cards.</param>
/// <param name="Back">Returns to the shop; printed tickets keep waiting.</param>
/// <param name="Refresh">Redraws this menu, for when a print was refused.</param>
internal sealed record TellerActions(Func<int, TarotId[]?> Print, Action<TarotId> Keep, Action<TarotId> Use, Action Back, Action Refresh);

/// <summary>The Fortune Teller's own menu. She prints one blind ticket for $3, or three for $6 to keep the one you like.
/// Printing charges and saves at once; the booth then glows, the tickets print face up out of its slot as one strip and are torn apart.</summary>
internal static class FortuneTellerScreen
{
    // The booth's artwork is 768×702. Points on it, in its own pixels, are scaled to where it is drawn.
    private const double ArtWidth = 440, ArtHeight = ArtWidth * 702 / 768, K = ArtWidth / 768;
    private const double StageWidth = 560, ArtX = (StageWidth - ArtWidth) / 2, TicketWidth = 104, TicketHeight = 156, Gap = 24;
    private const double RowY = ArtHeight + 14, StageHeight = RowY + TicketHeight + 16;
    // How large a ticket is while it is still in the slot: as wide as the slot's opening.
    private const double Feed = .48;
    private static readonly Point Crystal = new(ArtX + 385 * K, 418 * K), Slot = new(ArtX + 550 * K, 585 * K);
    private static readonly Point[] Lanterns = [new(ArtX + 45 * K, 262 * K), new(ArtX + 722 * K, 262 * K)];
    private static readonly IBrush TicketInk = Brush.Parse("#C792FF");
    private static readonly IBrush Danger = Brush.Parse("#FF617B");

    public static Control Build(CampaignRun run, ShopOffer offer, TellerActions actions, PixelMotion motion, string? note = null)
    {
        var money = run.Money;
        var stop = new CancellationTokenSource();
        var stage = new Canvas { Width = StageWidth, Height = StageHeight, ClipToBounds = false };
        void Add(Control control, double x, double y) { Canvas.SetLeft(control, x); Canvas.SetTop(control, y); stage.Children.Add(control); }

        var sway = new TranslateTransform();
        var booth = PixelArt.Machine("machine-fortune-teller-ticket-dispenser", ArtWidth, ArtHeight);
        booth.RenderTransform = sway;
        var lanterns = Lanterns.Select(_ => MachineFx.Glow(70, Color.Parse("#FFD27A"))).ToArray();
        for (var i = 0; i < lanterns.Length; i++) Add(lanterns[i], Lanterns[i].X - 35, Lanterns[i].Y - 35);
        Add(booth, ArtX, 0);
        var aura = MachineFx.Glow(150, Color.Parse("#E0B0FF"));
        var swell = new ScaleTransform(1, 1);
        aura.RenderTransform = swell;
        Add(aura, Crystal.X - 75, Crystal.Y - 75);
        var sparks = Enumerable.Range(0, 5).Select(i => MachineFx.Star(i % 2 == 0 ? 16 : 11, i % 2 == 0 ? Gold : Brushes.White)).ToArray();
        foreach (var spark in sparks) Add(spark, Crystal.X, Crystal.Y);
        // Lanterns burn softly while she waits.
        foreach (var lantern in lanterns) lantern.Opacity = .35;

        var detail = new Border
        {
            Width = 300, MinHeight = 300, Padding = new Thickness(16), VerticalAlignment = VerticalAlignment.Top,
            Background = Brush.Parse("#CC100B28"), BorderBrush = MainWindow.Line, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(2)
        };
        var moneyLabel = CampaignShopScreen.Label($"${money}", Gold, 26);
        moneyLabel.Name = "TellerMoney"; moneyLabel.VerticalAlignment = VerticalAlignment.Center;
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { MenuParts.Title(motion, "FORTUNE TELLER", 34), moneyLabel }
        };
        var back = MenuParts.Button("Back to Shop", false, motion, "TellerBack", actions.Back, false, 260);
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24, 16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(header);
        if (note is not null)
        {
            var line = CampaignShopScreen.Label(note, note.EndsWith('!') ? MainWindow.Green : Danger, 12);
            line.Name = "CampaignNote"; line.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(line);
        }
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center, Children = { stage, detail } });
        panel.Children.Add(back);
        panel.DetachedFromVisualTree += (_, _) => stop.Cancel();

        // The printed tickets, once they lie in the row and can be chosen.
        var tickets = new List<(TarotId Id, Button Button, Border Frame)>();
        void Choose(TarotId? chosen)
        {
            foreach (var (id, button, frame) in tickets)
            {
                frame.BorderBrush = id == chosen ? Gold : TicketInk;
                ((TranslateTransform)button.RenderTransform!).Y = id == chosen ? -10 : 0;
            }
            detail.Child = chosen is { } ticket ? TicketDetail(ticket) : PickDetail();
        }
        Control PickDetail() => Detail("YOUR TICKETS", TicketInk, "Pick one",
            "Press a ticket to read it. Keep one for a match, or use it now on six offered cards. Use Now commits that ticket; apply it before leaving. The other printed tickets are lost.", null);
        Control TicketDetail(TarotId ticket)
        {
            var definition = FeatureCatalog.Get(FeatureCategory.Tarot, ticket.ToString());
            var room = run.Resources.Inventory.HasItemRoom;
            var keep = MenuParts.Button("Keep", false, motion, "TellerKeep", () => actions.Keep(ticket), true, 264);
            keep.IsEnabled = room;
            var use = MenuParts.Button("Use Now", true, motion, "TellerUse", () => actions.Use(ticket), true, 264);
            return Detail("TICKET", TicketInk, definition.Name, definition.Description,
                room ? "Use Now opens six offered cards and commits this ticket. Apply it before leaving; inventory tickets can share the same cards." : "Your card slots are full. Use Now needs no slot and commits this ticket: apply it before leaving. Inventory tickets can share its six cards.",
                [keep, use], !room);
        }
        Button TicketButton(TarotId id, Border face)
        {
            var button = new Button
            {
                Name = $"Ticket{id}", Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand),
                RenderTransform = new TranslateTransform()
            };
            CampaignCardFocus.Apply(button, face);
            AutomationProperties.SetName(button, $"Ticket: {FeatureCatalog.Get(FeatureCategory.Tarot, id.ToString()).Name}");
            button.Click += (_, _) => Choose(id);
            tickets.Add((id, button, face));
            return button;
        }
        static double RowX(int index, int count) => (StageWidth - (count * TicketWidth + (count - 1) * Gap)) / 2 + index * (TicketWidth + Gap);

        // Tickets already printed (after going back to the shop, or reloading) wait face up in the row.
        void Lay(TarotId[] printed)
        {
            for (var i = 0; i < printed.Length; i++)
            {
                var button = TicketButton(printed[i], Front(printed[i]));
                Add(button, RowX(i, printed.Length) - 3, RowY - 3);
            }
            Choose(printed.Length == 1 ? printed[0] : null);
        }

        Control PrintDetail()
        {
            var one = MenuParts.Button($"Print 1 · ${CampaignShop.TicketPrice}", false, motion, "TellerPrintOne", () => Print(1), true, 264);
            var three = MenuParts.Button($"Print 3 · ${CampaignShop.TicketSpreadPrice}", true, motion, "TellerPrintThree", () => Print(CampaignShop.TicketSpread), true, 264);
            one.IsEnabled = money >= CampaignShop.TicketPrice;
            three.IsEnabled = money >= CampaignShop.TicketSpreadPrice;
            var hint = money < CampaignShop.TicketPrice ? $"You need ${CampaignShop.TicketPrice - money} more."
                : money < CampaignShop.TicketSpreadPrice ? $"Three tickets need ${CampaignShop.TicketSpreadPrice - money} more."
                : "She prints once a visit. A reroll brings her back.";
            return Detail("FORTUNE TELLER", TicketInk, "Blind tickets",
                $"Print one mystery ticket for ${CampaignShop.TicketPrice}, or print {CampaignShop.TicketSpread} different tickets for ${CampaignShop.TicketSpreadPrice} and keep the one you want.",
                hint, [one, three], money < CampaignShop.TicketPrice);
        }

        var printing = false;
        async void Print(int count)
        {
            if (printing) return;
            printing = true;
            if (actions.Print(count) is not { } printed) { actions.Refresh(); return; }
            money -= CampaignShop.PrintPrice(count);
            moneyLabel.Text = $"${money}";
            detail.Child = Detail("FORTUNE TELLER", TicketInk, "Printing…", "The crystal ball is choosing your fortune.", null);
            if (!motion.Enabled) { Lay(printed); return; }
            panel.IsHitTestVisible = false;
            try { await Perform(printed); }
            catch (OperationCanceledException) { return; }
            panel.IsHitTestVisible = true;
            Lay(printed);
            tickets.FirstOrDefault().Button?.Focus();
        }

        // The crystal ball wakes and the booth rattles, then the tickets print and are torn off one by one into the row.
        async Task Perform(TarotId[] printed)
        {
            var token = stop.Token;
            await Tween.Run(1300, token, t =>
            {
                var wake = MachineFx.Part(t, 0, .35);
                var pulse = .5 + .5 * Math.Sin(t * Math.PI * 7);
                aura.Opacity = wake * (.55 + .45 * pulse);
                swell.ScaleX = swell.ScaleY = .7 + .5 * wake + .12 * pulse;
                foreach (var (lantern, i) in lanterns.Select((l, i) => (l, i))) lantern.Opacity = .35 + .6 * wake * Math.Abs(Math.Sin(t * 23 + i * 1.7));
                var rattle = Math.Sin(Math.PI * MachineFx.Part(t, .45, 1));
                sway.X = Math.Sin(t * 70) * 2.5 * rattle;
                sway.Y = -Math.Abs(Math.Sin(t * 45)) * 1.5 * rattle;
                for (var i = 0; i < sparks.Length; i++)
                {
                    var angle = t * Math.PI * 4 + i * Math.PI * 2 / sparks.Length;
                    var radius = 44 + 10 * Math.Sin(t * 9 + i);
                    Canvas.SetLeft(sparks[i], Crystal.X + Math.Cos(angle) * radius - sparks[i].Width / 2);
                    Canvas.SetTop(sparks[i], Crystal.Y + Math.Sin(angle) * radius * .6 - sparks[i].Height / 2);
                    sparks[i].Opacity = wake * (.6 + .4 * Math.Sin(t * 30 + i * 2));
                }
            });

            // Printing: every ticket comes out at once, face up, as one strip joined at perforations, fed out of the slot in
            // short pushes like an arcade ticket printer. Above the slot's lip the strip is still inside the booth, so it is clipped.
            var n = printed.Length;
            double pw = (TicketWidth + 6) * Feed, ph = (TicketHeight + 6) * Feed;
            // Piece k hangs k pieces below the slot; the bottom piece came out first and goes to the first place in the row.
            TarotId Piece(int k) => printed[n - 1 - k];
            var strip = new Canvas { Width = pw, Height = ph * n };
            for (var k = 0; k < n; k++)
            {
                var piece = new Viewbox { Width = pw, Height = ph, Child = Front(Piece(k)) };
                Canvas.SetTop(piece, k * ph);
                strip.Children.Add(piece);
                if (k > 0) strip.Children.Add(Perforation(pw, k * ph));
            }
            var mouth = new Canvas { Width = pw + 8, Height = ph * n + 8, ClipToBounds = true, IsHitTestVisible = false, Children = { strip } };
            Canvas.SetLeft(strip, 4);
            Add(mouth, Slot.X - mouth.Width / 2, Slot.Y);
            var head = MachineFx.Glow(90, Color.Parse("#FFE6A8"));
            Add(head, Slot.X - 45, Slot.Y - 45);
            var feedMs = 500 + 450 * n;
            await Tween.Run(feedMs, token, t =>
            {
                var pulse = .5 + .5 * Math.Sin(t * Math.PI * 9);
                aura.Opacity = .6 + .35 * pulse;
                Orbit(t * feedMs / 1000);
                // Short pushes with a pause between each.
                var steps = 4 * n + 3;
                var k = Math.Floor(t * steps);
                var pushed = t >= 1 ? 1 : (k + MachineFx.EaseOut(Math.Min(1, (t * steps - k) * 1.8))) / steps;
                Canvas.SetTop(strip, -ph * n * (1 - pushed));
                sway.X = t < 1 ? Math.Sin(t * feedMs * .35) * 1.2 : 0;
                head.Opacity = .55 + .3 * Math.Sin(t * feedMs * .05);
            });
            sway.X = 0;

            // Now outside the booth, the strip becomes separate pieces at the same places, with the perforations between them.
            stage.Children.Remove(mouth);
            var pieces = new Border[n];
            var tilt = new RotateTransform[n];
            var size = new ScaleTransform[n];
            var holes = new Control?[n];
            for (var k = 0; k < n; k++)
            {
                tilt[k] = new RotateTransform(); size[k] = new ScaleTransform(Feed, Feed);
                pieces[k] = Front(Piece(k));
                // Turned and scaled about its top edge, where it hangs from.
                pieces[k].RenderTransform = new TransformGroup { Children = { size[k], tilt[k] } };
                pieces[k].RenderTransformOrigin = new RelativePoint(.5, 0, RelativeUnit.Relative);
                Add(pieces[k], Slot.X - pieces[k].Width / 2, Slot.Y + k * ph);
                if (k > 0) { holes[k] = Perforation(pw, 0); Add(holes[k]!, Slot.X - pw / 2, Slot.Y + k * ph); }
            }
            var bursts = Enumerable.Range(0, n).Select(_ => MachineFx.Burst(TicketHeight * 1.7, Gold)).ToArray();
            for (var j = 0; j < n; j++)
            {
                stage.Children.Insert(stage.Children.IndexOf(pieces[0]), bursts[j]);
                Canvas.SetLeft(bursts[j], RowX(j, n) + TicketWidth / 2 - bursts[j].Width / 2);
                Canvas.SetTop(bursts[j], RowY + TicketHeight / 2 - bursts[j].Height / 2);
            }
            // Scraps of paper fly from each tear.
            var scraps = Enumerable.Range(0, n * 4).Select(i => MachineFx.Star(i % 2 == 0 ? 9 : 6, i % 3 == 0 ? Gold : Brushes.White)).ToArray();
            foreach (var scrap in scraps) Add(scrap, 0, 0);

            // Tearing: from the bottom up, each piece wrenches back and forth at its perforation, rips free and flies to its place.
            const double hangMs = 260, tearMs = 420, gapMs = 360, flyMs = 560, flashMs = 480;
            var total = hangMs + gapMs * (n - 1) + tearMs + flyMs + flashMs;
            await Tween.Run((int)total, token, t =>
            {
                var now = t * total;
                var settle = MachineFx.Part(now, total - flyMs - flashMs, total);
                aura.Opacity = .9 * (1 - settle) + .25 * settle;
                foreach (var spark in sparks) spark.Opacity = 1 - settle;
                foreach (var lantern in lanterns) lantern.Opacity = .35 + .25 * (1 - settle);
                Orbit((feedMs + now) / 1000);
                for (var j = 0; j < n; j++)
                {
                    var k = n - 1 - j;
                    var local = now - hangMs - j * gapMs;
                    var hang = new Point(Slot.X, Slot.Y + k * ph);
                    if (local <= 0) continue;
                    var tear = Math.Min(1, local / tearMs);
                    var fly = MachineFx.EaseOut(Math.Clamp((local - tearMs) / flyMs, 0, 1));
                    // Two hard tugs, then the rip.
                    var tug = Math.Sin(tear * Math.PI * 4) * 7 * (1 - tear);
                    var pull = tear < .75 ? 0 : MachineFx.EaseOut((tear - .75) / .25) * 10;
                    if (tear >= .75 && holes[k] is { } hole) { stage.Children.Remove(hole); holes[k] = null; }
                    var ripped = MachineFx.Part(local, tearMs * .75, tearMs * .75 + 380);
                    for (var q = 0; q < 4; q++)
                    {
                        var scrap = scraps[j * 4 + q];
                        var dir = (q - 1.5) * .9;
                        Canvas.SetLeft(scrap, hang.X + Math.Sin(dir) * 40 * ripped + (q - 1.5) * pw / 4 - scrap.Width / 2);
                        Canvas.SetTop(scrap, hang.Y - 18 * Math.Sin(Math.PI * ripped) + 30 * ripped * ripped - scrap.Height / 2);
                        scrap.Opacity = ripped <= 0 || ripped >= 1 ? 0 : 1 - ripped;
                    }
                    var to = new Point(RowX(j, n) + (TicketWidth + 6) / 2, RowY);
                    var x = hang.X + (to.X - hang.X) * fly;
                    var y = hang.Y + pull + (to.Y - hang.Y - pull) * fly - Math.Sin(Math.PI * fly) * 60;
                    Canvas.SetLeft(pieces[k], x - pieces[k].Width / 2);
                    Canvas.SetTop(pieces[k], y);
                    size[k].ScaleX = size[k].ScaleY = Feed + (1 - Feed) * MachineFx.EaseBack(fly);
                    tilt[k].Angle = tug + Math.Sin(Math.PI * fly) * (to.X < hang.X ? 12 : -12);
                    var flash = Math.Clamp((local - tearMs - flyMs * .7) / flashMs, 0, 1);
                    MachineFx.Spin(bursts[j], now * .09 + j * 20, .4 + .8 * MachineFx.EaseOut(flash), flash <= 0 || flash >= 1 ? 0 : .9 * Math.Sin(Math.PI * flash));
                }
                head.Opacity = Math.Max(0, .5 * (1 - now / 400));
            });
            foreach (var control in pieces.Concat<Control>(bursts).Concat(scraps).Append(head)) stage.Children.Remove(control);
            aura.Opacity = .25;
        }

        void Orbit(double seconds)
        {
            for (var i = 0; i < sparks.Length; i++)
            {
                var angle = seconds * Math.PI * 3 + i * Math.PI * 2 / sparks.Length;
                Canvas.SetLeft(sparks[i], Crystal.X + Math.Cos(angle) * 46 - sparks[i].Width / 2);
                Canvas.SetTop(sparks[i], Crystal.Y + Math.Sin(angle) * 28 - sparks[i].Height / 2);
            }
        }

        if (offer.Printed is { } waiting) Lay(waiting.Select(Enum.Parse<TarotId>).ToArray());
        else detail.Child = PrintDetail();
        return MenuParts.Scene(motion, panel, new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
    }

    /// <summary>A printed ticket, face up, in its frame.</summary>
    private static Border Front(TarotId id)
    {
        var art = PixelArt.CampaignIcon(FeatureCatalog.Get(FeatureCategory.Tarot, id.ToString()), TicketHeight - 8);
        return new Border
        {
            Width = TicketWidth + 6, Height = TicketHeight + 6, Background = MainWindow.Panel, BorderBrush = TicketInk, BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = art
        };
    }

    /// <summary>The dashed tear line between two printed tickets still joined in their strip.</summary>
    private static Line Perforation(double width, double y) => new()
    {
        StartPoint = new Point(1, y), EndPoint = new Point(width - 1, y), Stroke = Brush.Parse("#100B28"), StrokeThickness = 2,
        StrokeDashArray = [2, 1.5], IsHitTestVisible = false
    };

    private static Control Detail(string kind, IBrush ink, string name, string description, string? hint, Control[]? buttons = null, bool warning = false)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(CampaignShopScreen.Label(kind, ink, 11));
        var title = CampaignShopScreen.Label(name, Gold, 20);
        title.Name = "TellerHeading";
        stack.Children.Add(title);
        stack.Children.Add(new TextBlock { Text = description, Foreground = MainWindow.Ink, FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        if (hint is not null) stack.Children.Add(new TextBlock { Text = hint, Foreground = warning ? Danger : Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        foreach (var button in buttons ?? []) { button.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(button); }
        return stack;
    }
}
