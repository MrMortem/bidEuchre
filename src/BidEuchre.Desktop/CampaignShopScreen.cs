using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.App;
using BidEuchre.Core;
using static BidEuchre.Desktop.MainWindow;
namespace BidEuchre.Desktop;

/// <summary>What the shop screen can ask of the run. Each one redraws the shop when it is done.</summary>
internal sealed record ShopActions(Action<string> Buy, Action<string> BuyAndUse, Action<JokerId> Sell,
    Action<FeatureCategory, int> DiscardItem, Action Inspect, Action Next)
{
    /// <summary>Pays for a gumball and saves the run, without redrawing. Null when the pull was refused.</summary>
    public required Func<string, GumballId?> Pull { get; init; }
    /// <summary>Redraws the shop, after the pull has been shown.</summary>
    public required Action Refresh { get; init; }
    public Action? OpenSoda { get; init; }
    public Action? BuySoda { get; init; }
    public Action? Reroll { get; init; }
    /// <summary>Opens the Fortune Teller's own menu for her offer.</summary>
    public Action<string>? Teller { get; init; }
    public Action? Arcade { get; init; }
    public Action<int>? OpenBooster { get; init; }
}

/// <summary>The balance the shop last showed during one visit, so a redraw after a purchase counts from it.</summary>
internal sealed class ShopPurse
{
    public object? Visit;
    public int? Shown;
}

/// <summary>The shop between matches: Jokers, the Arcade, the Fortune Teller and a gumball machine.</summary>
internal static class CampaignShopScreen
{
    private const double CardWidth = 148, CardHeight = 196, Art = 128, SmallCard = 76, Lift = 8;
    private static readonly IBrush Danger = Brush.Parse("#FF617B");
    private static readonly IBrush TarotInk = Brush.Parse("#C792FF");
    private static readonly IBrush SigilInk = Brush.Parse("#7BE0A0");

    public static Control Build(CampaignRun run, ShopActions actions, PixelMotion motion, string? note = null, ShopPurse? purse = null, bool showSoda = false)
    {
        var inventory = run.Resources.Inventory;
        var detail = new Border
        {
            Width = 300, MinHeight = CardHeight * 2 + 14, Padding = new Thickness(16), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, Lift, 0, 0),
            Background = Brush.Parse("#CC100B28"), BorderBrush = MainWindow.Line, BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(2)
        };
        var cards = new List<(Button Button, Border Face, IBrush Idle)>();

        // One card is selected at a time, across the offers and the player's own cards.
        void Select(Button? chosen, Control content)
        {
            foreach (var (button, face, rest) in cards)
            {
                var selected = ReferenceEquals(button, chosen);
                face.BorderBrush = selected ? Gold : rest;
                ((TranslateTransform)face.RenderTransform!).Y = selected && motion.Enabled ? -Lift : 0;
            }
            detail.Child = content;
        }
        Button Selectable(Border face, IBrush rest, string name, string label, Func<Control> describe)
        {
            face.RenderTransform = new TranslateTransform
            {
                Transitions =
                [
                    new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(120), Easing = new CubicEaseOut() }
                ]
            };
            var button = new Button
            {
                Name = name, Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand)
            };
            face.Margin = new Thickness(0, Lift, 0, 0);
            // The room above the card is what it rises into when selected.
            CampaignCardFocus.Apply(button, new Panel { Children = { face } });
            AutomationProperties.SetName(button, label);
            button.Click += (_, _) => Select(button, describe());
            cards.Add((button, face, rest));
            return button;
        }

        Control Offer(ShopOffer offer)
        {
            if (offer.Category == FeatureCategory.Tarot || offer.IsTellerSigil) return Teller(offer);
            var definition = FeatureCatalog.Get(offer.Category, offer.FeatureId);
            var price = CampaignShop.OfferPrice(run, offer);
            var ink = Ink(offer.Category);
            var blocked = offer.Sold ? null : run.Money < price ? "Not enough money"
                : offer.Category == FeatureCategory.Joker && inventory.Jokers.Length >= inventory.JokerCapacity ? "Sell a Joker first"
                : offer.Category == FeatureCategory.Sigil && !inventory.HasItemRoom ? "Card slots full" : null;
            var tag = offer.Sold ? "SOLD" : $"${price}";
            var face = definition.Category == FeatureCategory.Sigil
                ? Face("SPECTRAL", ink, PixelArt.Machine(PixelArt.CollectorImageName(definition.Key), Art, Art),
                    definition.Name + " Sigil", tag, blocked)
                : Face(definition, ink, tag, blocked);
            if (offer.Sold) { face.Opacity = .4; face.Margin = new Thickness(0, Lift, 0, 0); return face; }
            return Selectable(face, ink, $"Offer{offer.Id}", $"{CampaignTableControls.CategoryName(offer.Category)}: {definition.Name}, ${price}{(blocked is null ? "" : ", " + blocked)}",
                () => OfferDetail(run, offer, definition, actions, motion));
        }

        // The Fortune Teller has a menu of her own; her card only says whether she is open.
        Control Teller(ShopOffer offer)
        {
            var waiting = offer.Printed is not null;
            var tag = offer.Sold ? "SOLD" : waiting ? "PICK" : $"${CampaignShop.PrintPrice(run, 1)}+";
            var blocked = offer.Sold || waiting ? null : run.Money < CampaignShop.PrintPrice(run, 1) ? "Not enough money" : null;
            var face = Face("FORTUNE", TarotInk, PixelArt.Machine("machine-fortune-teller-ticket-dispenser", Art, Art),
                waiting ? "Tickets waiting" : "Fortune Teller", tag, blocked);
            face.Name = "FortuneTellerFace";
            if (offer.Sold) { face.Opacity = .4; face.Margin = new Thickness(0, Lift, 0, 0); return face; }
            face.RenderTransform = new TranslateTransform();
            face.Margin = new Thickness(0, Lift, 0, 0);
            var button = new Button { Name = "FortuneTeller", Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand) };
            CampaignCardFocus.Apply(button, new Panel { Children = { face } });
            AutomationProperties.SetName(button, waiting ? "Fortune Teller: your printed tickets are waiting" : $"Fortune Teller: blind tickets from ${CampaignShop.PrintPrice(run, 1)}");
            button.Click += (_, _) => actions.Teller?.Invoke(offer.Id);
            return button;
        }

        Control Booster(JokerBooster booster, int index)
        {
            var name = BoosterName(booster);
            var price = CampaignShop.Discount(run, CampaignShop.BoosterPrice);
            var blocked = booster.Sold ? null : run.Shop!.Offers.Any(o => o.Printed is not null) ? "Choose a printed ticket first"
                : run.Money < price ? "Not enough money" : null;
            var face = Face("JOKER BOOSTER", Gold, PixelArt.Machine("card-pack-" + booster.Theme, Art, Art),
                name, booster.Sold ? "SOLD" : $"${price}", blocked);
            if (booster.Sold) { face.Opacity = .4; face.Margin = new Thickness(0, Lift, 0, 0); return face; }
            return Selectable(face, Gold, index == 0 ? "ShopBooster" : "ShopBonusBooster", $"{name}, ${price}. {blocked}", () =>
            {
                var open = MenuParts.Button($"Buy ${price}", true, motion, "OpenBooster", () => actions.OpenBooster?.Invoke(index), true, 264);
                open.IsEnabled = blocked is null && actions.OpenBooster is not null;
                return Detail("JOKER BOOSTER", Gold, name, $"Reveal {CampaignShop.BoosterChoices(inventory)} random Jokers you don’t own. Keep one or skip the pack.",
                    blocked, [open], blocked is not null);
            });
        }

        Control Arcade()
        {
            var arcade = run.Shop!.Arcade;
            var left = (arcade?.PlinkoPlayed == true ? 0 : 1) + (arcade?.RoulettePlayed == true ? 0 : 1);
            var price = CampaignArcade.EntryPrice(run);
            var entry = price == 0 ? "Entry paid" : $"${price} entry";
            var face = Face("ARCADE", Gold, new Viewbox { Width = Art, Height = Art, Child = new PlinkoControl() },
                "Plinko + Roulette", entry, left == 0 ? "Played this visit" : null);
            face.Name = "ArcadeFace";
            face.Margin = new Thickness(0, Lift, 0, 0);
            var button = new Button { Name = "ShopArcade", Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand) };
            CampaignCardFocus.Apply(button, new Panel { Children = { face } });
            AutomationProperties.SetName(button, $"Arcade: Plinko and Roulette, {entry}, one play of each game per shop visit");
            button.Click += (_, _) => actions.Arcade?.Invoke();
            return button;
        }

        // The machine sells one blind pull a visit. The run is paid and saved before the gumball rolls out.
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24, 16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { MenuParts.Title(motion, "SHOP", 38) }
        };
        var money = Label("", Gold, 26);
        money.Name = "ShopMoney";
        var wallet = new NumberTally(money, value => $"${value}", change => change > 0 ? $"+${change}" : $"−${-change}", 16);
        wallet.Host.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(wallet.Host);
        // A purchase redraws the shop: count from the balance shown before it.
        wallet.Set(purse?.Shown ?? run.Money);
        if (purse is not null) purse.Shown = run.Money;
        if (wallet.Value != run.Money)
            wallet.Host.AttachedToVisualTree += (_, _) => wallet.Change(run.Money, motion.Enabled);
        var locked = Enum.GetValues<GumballId>().Except(inventory.Unlocks).ToArray();
        Control? Machine()
        {
            var offer = run.Shop!.Offers.SingleOrDefault(o => o.Category == FeatureCategory.Gumball);
            if (offer is null) return null;
            var price = CampaignShop.OfferPrice(run, offer);
            var open = !offer.Sold;
            var machine = new GumballMachine(locked.Length > 0 ? locked : Enum.GetValues<GumballId>(), open ? $"${price}" : "SOLD");
            if (!open) { machine.Face.Opacity = .4; machine.Face.Margin = new Thickness(0, Lift, 0, 0); return machine.Face; }
            if (run.Money < price) ((Grid)machine.Face.Child!).Children.Add(Badge("Not enough money", Danger));
            var pulling = false;
            async void Pull()
            {
                if (pulling) return;
                pulling = true;
                if (actions.Pull(offer!.Id) is not { } gumball || !motion.Enabled) { actions.Refresh(); return; }
                // Paid already: the purse shows it while the gumball rolls out.
                wallet.Change(run.Money - price, true);
                if (purse is not null) purse.Shown = run.Money - price;
                panel.IsHitTestVisible = false;
                if (await machine.Dispense(gumball, FeatureCatalog.Get(FeatureCategory.Gumball, gumball.ToString()).Name,
                    locked.Contains(gumball) ? "UNLOCKED!" : $"COPY {inventory.GumballCopies(gumball) + 1}!")) actions.Refresh();
            }
            return Selectable(machine.Face, MainWindow.Green, "GumballMachine", $"Gumball machine: mystery bid, ${price}{(run.Money < price ? ", Not enough money" : "")}", () =>
            {
                var pull = MenuParts.Button($"Pull ${price}", true, motion, "PullGumball", Pull, true, 264);
                pull.IsEnabled = run.Money >= price;
                return Detail("GUMBALL", MainWindow.Green, "Mystery bid",
                    locked.Length > 0
                        ? $"Unlock a random missing bid. {locked.Length} of {CampaignInventory.GumballCount} bids remain locked."
                        : $"All {CampaignInventory.GumballCount} bids are unlocked. Pull a random duplicate: each extra copy earns +1 point when you successfully make its matching bid, after hand multipliers.",
                    pull.IsEnabled ? "Unlocks and duplicate counts last for the run. Partner bids earn no duplicate bonus." : $"You need ${price - run.Money} more.", [pull], !pull.IsEnabled);
            });
        }

        var jokers = Row(14);
        var others = Row(14);
        var sodaPrice = CampaignShop.SodaPrice(run);
        var available = CampaignShop.EligibleSodas(inventory);
        var sodaSold = run.Shop!.SodaPurchased || available.Length == 0;
        var blockedSoda = sodaSold ? "Sold out" : run.Shop.Offers.Any(o => o.Printed is not null) ? "Choose a printed ticket first" : run.Money < sodaPrice ? "Not enough money" : null;
        SodaId? dispensed = run.Shop.SodaPurchased && inventory.Sodas.Length > 0 ? inventory.Sodas[^1] : null;
        Control VendingDetail()
        {
            var buy = MenuParts.Button($"Buy soda · ${sodaPrice}", true, motion, "BuySoda", () => actions.BuySoda?.Invoke(), true, 264);
            buy.IsEnabled = blockedSoda is null && actions.BuySoda is not null;
            if (dispensed.HasValue)
                return Detail("SODA", MainWindow.Green, "Vending Machine", "Click the can in the tray to inspect your upgrade.", null, buy);
            return Detail("SODA", MainWindow.Green, "Vending Machine",
                "Dispenses one random new soda per visit. Its upgrade lasts for the rest of your run. Each base soda unlocks its upgraded flavor.",
                blockedSoda, [buy], blockedSoda is not null);
        }
        var shopLayer = new Grid();
        var stocked = dispensed.HasValue
            ? CampaignShop.EligibleSodas(inventory with { Sodas = inventory.Sodas[..^1] })
            : sodaSold ? [] : available;
        var opened = run.Shop.SodaOpened;
        var vending = SodaArtwork.Machine(stocked, dispensed, sodaSold ? "SOLD" : $"${sodaPrice}", motion, showSoda,
            () => Select(null, VendingDetail()), (id, tray, art) =>
            {
                if (shopLayer.Children.Count != 1) return;
                SodaArtwork.Inspect(shopLayer, tray, art, id, motion, !opened);
                if (!opened) actions.OpenSoda?.Invoke();
                opened = true;
            });
        // Offers drift on the shelf like cards at the table; sold ones rest.
        var shelved = 0;
        Control Shelved(ShopOffer offer) => offer.Sold ? Offer(offer) : motion.Float(Offer(offer), 20 + shelved++);
        foreach (var offer in run.Shop!.Offers.Where(o => o.Category == FeatureCategory.Joker && !o.IsArcadePrize)) jokers.Children.Add(Shelved(offer));
        if (CampaignArcade.Available(run)) others.Children.Add(motion.Float(Arcade(), 20 + shelved++));
        foreach (var offer in run.Shop.Offers.Where(o => o.Category is FeatureCategory.Tarot or FeatureCategory.Sigil && !o.IsArcadePrize)) others.Children.Add(Shelved(offer));
        void AddBooster(JokerBooster? booster, int index)
        {
            if (booster is null) return;
            var card = Booster(booster, index);
            var shelf = jokers.Children.Count < others.Children.Count ? jokers : others;
            shelf.Children.Add(booster.Sold ? card : motion.Float(card, 20 + shelved++));
        }
        AddBooster(run.Shop.Booster, 0);
        AddBooster(run.Shop.BonusBooster, 1);
        // The machine stands beside the cards, as tall as both rows.
        var offers = Row(14);
        offers.VerticalAlignment = VerticalAlignment.Top;
        offers.Children.Add(new StackPanel { Spacing = 14 - Lift, Children = { jokers, others } });
        offers.Children.Add(vending);
        if (Machine() is { } gumballs) offers.Children.Add(gumballs);

        var ownedJokers = new List<Control>();
        foreach (var joker in inventory.Jokers)
        {
            var definition = FeatureCatalog.Get(FeatureCategory.Joker, joker.Id.ToString());
            var value = joker.DisplayValue();
            var face = OwnedJokerFace(definition, true);
            ownedJokers.Add(Selectable(face, Gold, $"Owned{joker.Id}", "Your Joker: " + definition.Name + ", sell for $2", () => Detail(
                "YOUR JOKER", Gold, definition.Name, value == 0 ? definition.Description : $"{definition.Description}\nCurrent value: {value}", null,
                MenuParts.Button("Sell +$2", false, motion, $"Sell{joker.Id}", () => actions.Sell(joker.Id), true, 264))));
        }
        // The ordinary shelf only offers discard; inventory tickets become usable inside a purchase's six-card menu.
        var ownedItems = new List<Control>();
        foreach (var (category, slot, definition) in CampaignTableControls.Items(inventory))
        {
            var ink = Ink(category);
            ownedItems.Add(Selectable(CampaignTableControls.CardFace(definition), ink, $"Owned{category}{slot}", $"Your {CampaignTableControls.CategoryName(category)}: {definition.Name}", () => Detail(
                $"YOUR {CampaignTableControls.CategoryName(category).ToUpperInvariant()}", ink, definition.Name, definition.Description,
                category == FeatureCategory.Tarot ? "Use during a match on cards in your hand, or while a purchase's six-card menu is open." : "Use during a match on cards in your hand.",
                MenuParts.Button("Discard", false, motion, $"Discard{category}{slot}", () => actions.DiscardItem(category, slot), true, 264))));
        }

        // Each gumball pulled so far, in its own colour; its bid is in the tooltip.
        var pulled = new WrapPanel { Name = "OwnedGumballs", Width = 5 * 38, Height = SmallCard, Margin = new Thickness(0, Lift, 0, 0) };
        foreach (var id in inventory.Unlocks)
        {
            var definition = FeatureCatalog.Get(FeatureCategory.Gumball, id.ToString());
            var face = new StackPanel { Children = { GumballMachine.Ball(id, 34), Label($"×{inventory.GumballCopies(id)}", MainWindow.Ink, 9) } };
            var ball = new Border { Background = Brushes.Transparent, Padding = new Thickness(2), Child = face };
            var reward = $"Successful matching human bid: +{inventory.GumballExtraCopies.GetValueOrDefault(id)} points after hand multipliers. Partner bids do not qualify.";
            ToolTip.SetTip(ball, $"{definition.Name}: {definition.Description}\nCopies: {inventory.GumballCopies(id)}. {reward}"); ToolTip.SetShowDelay(ball, 120);
            pulled.Children.Add(ball);
        }
        var owned = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 28, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                Shelf($"GUMBALLS {inventory.Unlocks.Length}/{CampaignInventory.GumballCount}", pulled)
            }
        };

        panel.Children.Add(header);
        if (note is not null)
        {
            var line = Label(note, note.EndsWith('!') ? MainWindow.Green : Danger, 12);
            line.Name = "CampaignNote"; line.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(line);
        }
        var market = new Viewbox
        {
            StretchDirection = StretchDirection.DownOnly,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { offers, detail }
            }
        };
        panel.Children.Add(market);
        panel.Children.Add(owned);
        if (inventory.Sodas.Length > 0) panel.Children.Add(SodaArtwork.Shelf(inventory));
        var rerollPrice = CampaignShop.RerollPrice(run);
        var reroll = MenuParts.Button($"Reroll ${rerollPrice}", false, motion, "ShopReroll", () => actions.Reroll?.Invoke(), true, 260);
        reroll.IsEnabled = actions.Reroll is not null && run.Money >= rerollPrice;
        ToolTip.SetTip(reroll, run.Money < rerollPrice ? $"Not enough money. You need ${rerollPrice - run.Money} more."
            : "Restock individual Jokers and the Fortune Teller. The booster, arcade plays, and gumball pull are offered once per visit. The next reroll costs $1 more.");
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                reroll,
                MenuParts.Button("Inspect Deck", false, motion, "InspectDeck", actions.Inspect, true, 260),
                MenuParts.Button("Next Match", true, motion, "CampaignNextMatch", actions.Next, false, 260)
            }
        });
        if (showSoda) Select(null, VendingDetail());
        else Select(null, Detail("SHOP", Muted, "Pick a card", "Press a card to see what it does and what it costs.", null));
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        scroll.SizeChanged += (_, e) => market.MaxWidth = Math.Max(1, e.NewSize.Width - panel.Margin.Left - panel.Margin.Right - 16);
        shopLayer.Children.Add(scroll);
        return CampaignInventoryScreen.Build(MenuParts.Scene(motion, panel, shopLayer), inventory, motion, ownedJokers, ownedItems);
    }

    private static string BoosterName(JokerBooster booster) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(booster.Theme.Replace('-', ' ')) + " Booster";

    public static Control OpenedBooster(JokerBooster booster, CampaignInventory inventory, Action tear, Action<JokerId?> resolve,
        Action<JokerId> sell, PixelMotion motion, string? note, bool reveal)
    {
        var wrapperArt = "card-pack-" + booster.Theme;
        var full = inventory.Jokers.Length >= inventory.JokerCapacity;
        var shelf = new List<Control>();
        var sale = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var state in inventory.Jokers)
        {
            var definition = FeatureCatalog.Get(FeatureCategory.Joker, state.Id.ToString());
            var canSell = full && !booster.AwaitingOpen;
            var face = OwnedJokerFace(definition, canSell);
            ToolTip.SetTip(face, $"{definition.Name}: {definition.Description}");
            if (canSell)
            {
                var card = new Button { Name = $"Owned{state.Id}", Padding = new Thickness(0) };
                CampaignCardFocus.Apply(card, face);
                AutomationProperties.SetName(card, $"Your Joker: {definition.Name}");
                card.Click += (_, _) => sale.Content = MenuParts.Button($"Sell {definition.Name} +${CampaignShop.JokerSellValue}", false,
                    motion, $"BoosterSell{state.Id}", () => sell(state.Id), true, 360);
                shelf.Add(card);
            }
            else shelf.Add(face);
        }
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(sale, 1);
        layout.Children.Add(sale);
        if (booster.AwaitingOpen)
        {
            var sealedPack = new Grid();
            sealedPack.Children.Add(BoosterOpening.Sealed("Booster", wrapperArt, BoosterName(booster), booster.Choices!.Length, motion, tear));
            if (note is not null)
            {
                var message = Label(note, Danger, 14);
                message.VerticalAlignment = VerticalAlignment.Top;
                message.HorizontalAlignment = HorizontalAlignment.Center;
                sealedPack.Children.Add(message);
            }
            layout.Children.Insert(0, sealedPack);
            return CampaignInventoryScreen.Build(MenuParts.Scene(motion, layout), inventory, motion, shelf);
        }
        var targets = new List<Image>();
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var header = Row(14);
        header.Children.Add(PixelArt.Machine("card-pack-" + booster.Theme, 48, 64));
        var title = MenuParts.Title(motion, BoosterName(booster).ToUpperInvariant(), 28);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        panel.Children.Add(header);
        var prompt = Label(full ? "Sell an owned Joker to keep a card, or skip the pack." : "Choose one Joker to keep.", Gold, 15);
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(prompt);
        if (note is not null) panel.Children.Add(Label(note, Danger, 12));
        var row = Row(10);
        panel.Children.Add(new Viewbox { StretchDirection = StretchDirection.DownOnly, Child = row });
        var effect = new TextBlock
        {
            Name = "BoosterEffect", FontSize = 17, Foreground = MainWindow.Ink, MinHeight = 64,
            MaxWidth = 660, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
        };
        panel.Children.Add(effect);
        foreach (var id in booster.Choices!)
        {
            var joker = FeatureCatalog.Get(FeatureCategory.Joker, id.ToString());
            var pick = MenuParts.Button("Keep " + joker.Name, false, motion, $"BoosterPick{id}", () => resolve(id), true, 210);
            pick.IsEnabled = !full;
            pick.VerticalAlignment = VerticalAlignment.Stretch;
            pick.Padding = new Thickness(10);
            pick.BorderBrush = Gold;
            pick.Cursor = new Cursor(StandardCursorType.Hand);
            pick.RenderTransform = null;
            var card = new StackPanel { Spacing = 6 };
            var art = PixelArt.CampaignIcon(joker, 168);
            art.Name = $"BoosterTarget{id}";
            targets.Add(art);
            card.Children.Add(art);
            card.Children.Add(Label(joker.Name, Gold, 15));
            pick.Content = card;
            BoosterOpening.EnableHoverLift(pick, motion);
            var slot = new Border { Background = Brushes.Transparent, Child = pick };
            BoosterOpening.ShowEffectOnHover(slot, pick, effect, $"{joker.Name} — {joker.Description}");
            row.Children.Add(slot);
        }
        var skip = MenuParts.Button("Skip pack", false, motion, "SkipBooster", () => resolve(null), true, 260);
        panel.Children.Add(skip);
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var interactive = !reveal || !motion.Lively;
        foreach (var card in shelf) card.IsEnabled = interactive;
        var host = interactive ? (Control)scroll : BoosterOpening.Reveal("Booster", wrapperArt, targets.ToArray(), scroll, motion, () =>
        {
            foreach (var card in shelf) card.IsEnabled = true;
            MenuParts.FocusFirst(panel);
        });
        layout.Children.Insert(0, host);
        return CampaignInventoryScreen.Build(MenuParts.Scene(motion, layout), inventory, motion, shelf);
    }

    private static Border OwnedJokerFace(FeatureDefinition definition, bool showSell)
    {
        var face = CampaignTableControls.CardFace(definition);
        if (showSell)
        {
            var icon = face.Child!;
            face.Child = null;
            face.Child = new Grid { Children = { icon, Badge($"Sell +${CampaignShop.JokerSellValue}", Gold) } };
        }
        return face;
    }

    // A new Sigil purchase can be played on one of its six offered cards without using a slot.
    private static Control OfferDetail(CampaignRun run, ShopOffer offer, FeatureDefinition definition, ShopActions actions, PixelMotion motion)
    {
        var inventory = run.Resources.Inventory;
        var price = CampaignShop.OfferPrice(run, offer);
        var afford = run.Money >= price;
        var room = offer.Category switch
        {
            FeatureCategory.Joker => inventory.Jokers.Length < inventory.JokerCapacity,
            FeatureCategory.Sigil => inventory.HasItemRoom,
            _ => true
        };
        var buy = MenuParts.Button($"Buy ${price}", true, motion, $"Buy{offer.Id}", () => actions.Buy(offer.Id), true, 264);
        buy.IsEnabled = afford && room;
        var buttons = new List<Control> { buy };
        var hint = offer.Category switch
        {
            FeatureCategory.Sigil => "Keep it for a match, or Buy & Use on one of six offered cards. Buy & Use pays immediately: apply the Sigil before leaving. Inventory tickets may share those six cards.",
            _ => null
        };
        if (offer.Category == FeatureCategory.Sigil)
        {
            var use = MenuParts.Button($"Buy & Use ${price}", true, motion, $"BuyUse{offer.Id}", () => actions.BuyAndUse(offer.Id), true, 264);
            use.IsEnabled = afford;
            buttons.Add(use);
        }
        var problem = !afford ? $"You need ${price - run.Money} more."
            : room ? null
            : offer.Category == FeatureCategory.Joker ? "Your Joker slots are full. Sell one first."
            : "Your card slots are full. Buy and use this offer now, or discard a held card to free a slot.";
        return Detail(CampaignTableControls.CategoryName(offer.Category).ToUpperInvariant(), Ink(offer.Category), definition.Name, definition.Description, problem ?? hint,
            [.. buttons], problem is not null);
    }

    private static Control Detail(string kind, IBrush ink, string name, string description, string? hint, params Control[] buttons) =>
        Detail(kind, ink, name, description, hint, buttons, false);

    private static Control Detail(string kind, IBrush ink, string name, string description, string? hint, Control[] buttons, bool warning)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Label(kind, ink, 11));
        stack.Children.Add(Label(name, Gold, 20));
        stack.Children.Add(new TextBlock { Text = description, Foreground = MainWindow.Ink, FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        if (hint is not null)
            stack.Children.Add(new TextBlock { Text = hint, Foreground = warning ? Danger : Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        foreach (var button in buttons) { button.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(button); }
        return stack;
    }

    internal static IBrush Ink(FeatureCategory category) => category switch
    {
        FeatureCategory.Joker => Gold, FeatureCategory.Tarot => TarotInk, FeatureCategory.Sigil => SigilInk, _ => MainWindow.Green
    };

    // A card: its kind, its picture at twice size, its name, and a price tag on the corner.
    private static Border Face(FeatureDefinition definition, IBrush ink, string tag, string? blocked = null) =>
        Face(CampaignTableControls.CategoryName(definition.Category).ToUpperInvariant(), ink, PixelArt.CampaignIcon(definition, Art), definition.Name, tag, blocked);

    private static Border Face(string kindText, IBrush ink, Control art, string title, string tag, string? blocked)
    {
        var name = Label(title, MainWindow.Ink, 10);
        name.TextAlignment = TextAlignment.Center; name.HorizontalAlignment = HorizontalAlignment.Center;
        name.MaxLines = 2; name.TextTrimming = TextTrimming.CharacterEllipsis;
        var kind = Label(kindText, ink, 9);
        kind.Margin = new Thickness(7, 5, 0, 0);
        var body = new StackPanel
        {
            Children =
            {
                new Border { Height = 22, Child = kind },
                new Border { BorderBrush = ink, BorderThickness = new Thickness(0, 2), Child = art, HorizontalAlignment = HorizontalAlignment.Center, Width = CardWidth - 6 },
                new Border { Height = CardHeight - Art - 22 - 10, Padding = new Thickness(6, 0), Child = name }
            }
        };
        name.VerticalAlignment = VerticalAlignment.Center;
        if (blocked is not null) name.Margin = new Thickness(0, 0, 0, 14);
        var price = new Border
        {
            Background = Gold, Padding = new Thickness(6, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = tag, Foreground = Paper, FontSize = 11, FontFamily = PixelArt.Font, FontWeight = FontWeight.Bold }
        };
        var content = new Grid { Children = { body, price } };
        if (blocked is not null) content.Children.Add(Badge(blocked, Danger));
        return new Border
        {
            Width = CardWidth, Height = CardHeight, Background = MainWindow.Panel, BorderBrush = ink, BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = content
        };
    }

    private static Border Badge(string text, IBrush ink) => new()
    {
        Background = Brush.Parse("#F0100B28"), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Bottom,
        IsHitTestVisible = false,
        Child = new TextBlock { Text = text, Foreground = ink, FontSize = 9, FontFamily = PixelArt.Font, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }
    };

    private static StackPanel Row(double spacing) => new() { Orientation = Orientation.Horizontal, Spacing = spacing, HorizontalAlignment = HorizontalAlignment.Center };

    private static StackPanel Shelf(string title, Control row) => new() { Spacing = 6, Children = { Label(title, Muted, 11), row } };

    internal static TextBlock Label(string text, IBrush brush, double size) => new()
    {
        Text = text, Foreground = brush, FontSize = size, FontFamily = PixelArt.Font, TextWrapping = TextWrapping.Wrap
    };
}

/// <summary>Shows either the read-only shared deck or a purchase's saved six-card offer.</summary>
internal static class CampaignDeckPicker
{
    private const double CardWidth = 84, CardHeight = 124;

    public static Control Build(IReadOnlyList<CardState> deck, int required, Action<CardId[]> confirm, Action cancel, PixelMotion motion,
        string title, string confirmText = "Confirm", bool offeredCards = false)
    {
        var chosen = new List<CardId>();
        var frames = new List<Border>();
        var choices = new List<(CardId Id, Button Button)>();
        var count = CampaignShopScreen.Label("", Muted, 12);
        count.HorizontalAlignment = HorizontalAlignment.Center;
        var ok = MenuParts.Button(confirmText, true, motion, "ConfirmDeckChoice", () => confirm(chosen.ToArray()), true, 260);
        void Refresh()
        {
            count.Text = required == 1 ? "CHOOSE A CARD" : $"CHOOSE {required} CARDS · {chosen.Count}/{required}";
            ok.IsEnabled = chosen.Count == required;
            foreach (var (id, button) in choices)
                button.IsEnabled = required == 1 || chosen.Count < required || chosen.Contains(id);
        }

        var cards = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        var suitRows = new Dictionary<Suit, StackPanel>();
        var offeredRow = new StackPanel { Name = "ShopOfferedCards", Orientation = Orientation.Horizontal };
        if (offeredCards) cards.Children.Add(offeredRow);
        else
        {
            // Deck inspection keeps each printed suit on its own row.
            foreach (var suit in Enum.GetValues<Suit>())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, MinHeight = CardHeight + 47 };
                suitRows.Add(suit, row);
                cards.Children.Add(row);
            }
        }
        var displayCards = offeredCards ? deck : deck.OrderBy(card => card.Face.Suit).ThenBy(card => card.Face.Rank).ThenBy(card => card.Id.Value).ToArray();
        foreach (var card in displayCards)
        {
            var row = offeredCards ? offeredRow : suitRows[card.Face.Suit];
            var frame = new Border
            {
                Width = CardWidth + 6, Height = CardHeight + 6, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(6), Child = PixelArt.CardContent(card.Face, card.Enhancement, card.Sigil)
            };
            frames.Add(frame);
            var marks = new StackPanel { Spacing = 1, Height = 30 };
            var tips = new StackPanel { Spacing = 4, MaxWidth = 260 };
            void Mark(FeatureCategory category, string key, IBrush brush)
            {
                var definition = FeatureCatalog.Get(category, key);
                var line = CampaignShopScreen.Label(definition.Name.ToUpperInvariant(), brush, 9);
                line.HorizontalAlignment = HorizontalAlignment.Center;
                marks.Children.Add(line);
                tips.Children.Add(new TextBlock { Text = $"{definition.Name}: {definition.Description}", Foreground = MainWindow.Ink, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            }
            if (card.Enhancement is { } enhancement) Mark(FeatureCategory.Enhancement, enhancement.ToString(), Gold);
            if (card.Sigil is { } sigil) Mark(FeatureCategory.Sigil, sigil.ToString(), Brush.Parse("#7BE0A0"));
            // The art layers ignore hit testing; the template needs a surface across the card face.
            var cell = new StackPanel { Name = $"PreviewCard{card.Id.Value}", Background = Brushes.Transparent, Spacing = 3, Margin = new Thickness(5, 4), Children = { motion.Float(frame, card.Id.Value), marks } };
            if (tips.Children.Count > 0) { ToolTip.SetTip(cell, tips); ToolTip.SetShowDelay(cell, 120); }
            if (required == 0) { row.Children.Add(cell); continue; }

            var button = new Button
            {
                Name = $"DeckCard{card.Id.Value}", Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand)
            };
            choices.Add((card.Id, button));
            CampaignCardFocus.Apply(button, cell);
            AutomationProperties.SetName(button, $"{Card.DisplayRank(card.Face.Rank)} of {card.Face.Suit}, card {card.Id.Value + 1}");
            button.Click += (_, _) =>
            {
                if (chosen.Remove(card.Id)) frame.BorderBrush = Brushes.Transparent;
                else
                {
                    // With a single target, pressing another card moves the choice.
                    if (required == 1 && chosen.Count == 1) { chosen.Clear(); foreach (var other in frames) other.BorderBrush = Brushes.Transparent; }
                    if (chosen.Count == required) return;
                    chosen.Add(card.Id); frame.BorderBrush = Gold;
                    motion.Pulse(frame, 1.07);
                }
                Refresh();
            };
            // A purchase's offer is dealt in; the full deck is too large to stagger.
            row.Children.Add(offeredCards ? motion.Enter(button, 120 + row.Children.Count * 55, 0, 26, row.Children.Count % 2 == 0 ? -4 : 4, new BackEaseOut(), 360) : button);
        }

        var heading = CampaignShopScreen.Label(title, Gold, 20);
        heading.HorizontalAlignment = HorizontalAlignment.Center; heading.TextAlignment = TextAlignment.Center;
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(24, 16), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(heading);
        if (offeredCards)
        {
            var offeredLabel = CampaignShopScreen.Label("SIX OFFERED CARDS", Muted, 12);
            offeredLabel.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(offeredLabel);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };
        if (required > 0) { panel.Children.Add(count); buttons.Children.Add(ok); Refresh(); }
        panel.Children.Add(cards);
        buttons.Children.Add(MenuParts.Button("Back", false, motion, "CancelDeckChoice", cancel, true, 260));
        panel.Children.Add(buttons);
        return MenuParts.Scene(motion, panel, new ScrollViewer
        {
            Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });
    }
}
