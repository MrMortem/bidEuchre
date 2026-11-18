using Avalonia;
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

internal sealed record ShopUseActions(Action<CardId[]> UsePurchased, Action<int, CardId[]> UseTarot, Action Finish);

/// <summary>A paid purchase stays on its six cards until it is used. Held tickets can share this opportunity.</summary>
internal static class CampaignShopUseScreen
{
    public static Control Build(CampaignRun run, ShopUseActions actions, PixelMotion motion, string? note = null)
    {
        var session = run.Shop?.UseSession ?? throw new GameRuleException("No purchase is waiting to be used.");
        var bought = FeatureCatalog.Get(session.Category, session.FeatureId);
        var cards = CampaignShop.UseSessionCards(run);
        var held = run.Resources.Inventory.Tarot;
        int? selectedSlot = session.PurchasedItemUsed && held.Length > 0 ? 0 : null;
        var selected = !session.PurchasedItemUsed || held.Length > 0;
        var chosen = new List<CardId>();
        var itemFrames = new List<(int? Slot, Border Frame)>();
        var cardButtons = new List<(CardState Card, Button Button, Border Frame, TextBlock Mark)>();
        FeatureDefinition? Current() => !selected ? null : selectedSlot is { } slot
            ? FeatureCatalog.Get(FeatureCategory.Tarot, held[slot].ToString()) : bought;
        int Required() => Current() is not { } item ? 0 : item.Category == FeatureCategory.Sigil ? 1
            : Tarot.TargetCount(Enum.Parse<TarotId>(item.Key));
        var title = Center("", Gold, 20);
        title.Name = "ShopUseItemName";
        var description = Center("", Ink, 13);
        description.MaxWidth = 850;
        var count = Center("", Muted, 12);
        count.Name = "ShopUseTargetCount";
        var use = MenuParts.Button("Use item", true, motion, "ConfirmDeckChoice", () =>
        {
            if (!selected || chosen.Count != Required()) return;
            if (selectedSlot is { } slot) actions.UseTarot(slot, chosen.ToArray());
            else actions.UsePurchased(chosen.ToArray());
        }, true, 260);

        void Refresh()
        {
            var item = Current();
            var required = Required();
            title.Text = item?.Name ?? "Purchase applied";
            description.Text = item?.Description ?? "All your tickets have been used. Press Done to return to the shop.";
            count.Text = !selected ? "SIX OFFERED CARDS" : required == 0 ? "NO CARD SELECTION NEEDED"
                : $"CHOOSE {required} {(required == 1 ? "CARD" : "CARDS")} · {chosen.Count}/{required}";
            use.IsEnabled = selected && chosen.Count == required;
            foreach (var (slot, frame) in itemFrames)
                frame.BorderBrush = selected && slot == selectedSlot ? Gold : MainWindow.Line;
            foreach (var (card, button, frame, mark) in cardButtons)
            {
                var position = chosen.IndexOf(card.Id);
                button.IsEnabled = selected && required > 0 && (required == 1 || chosen.Count < required || position >= 0);
                frame.BorderBrush = position >= 0 ? Gold : Brushes.Transparent;
                var preview = position < 0 || item is null ? null : item.Category == FeatureCategory.Sigil
                    ? card with { Sigil = Enum.Parse<SigilId>(item.Key) }
                    : Tarot.Preview(Enum.Parse<TarotId>(item.Key), card);
                var shown = preview ?? card;
                frame.Child = PixelArt.CardContent(shown.Face, shown.Enhancement, shown.Sigil);
                var marks = new List<string>();
                if (position >= 0) marks.Add($"{position + 1} · {(preview is null ? "RANDOM" : "PREVIEW")}");
                if (shown.Enhancement is { } enhancement) marks.Add(FeatureCatalog.Get(FeatureCategory.Enhancement, enhancement.ToString()).Name);
                if (shown.Sigil is { } sigil) marks.Add(FeatureCatalog.Get(FeatureCategory.Sigil, sigil.ToString()).Name);
                mark.Text = string.Join("\n", marks);
            }
        }

        Control Item(FeatureDefinition item, int? slot, bool available)
        {
            var frame = slot.HasValue ? CampaignTableControls.CardFace(item) : new Border
            {
                Padding = new Thickness(4), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4),
                Background = MainWindow.Panel, BorderBrush = MainWindow.Line,
                Child = new StackPanel
                {
                    Spacing = 3, Children =
                    {
                        PixelArt.CampaignIcon(item, 72),
                        Center(item.Name, Ink, 10)
                    }
                }
            };
            var button = new Button
            {
                Name = slot is { } index ? $"ShopHeldTarot{index}" : "ShopPurchasedItem",
                Padding = new Thickness(0), Width = slot.HasValue ? double.NaN : 118, IsEnabled = available,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            CampaignCardFocus.Apply(button, frame);
            AutomationProperties.SetName(button, $"{(slot is null ? "Purchased" : "Inventory ticket")}: {item.Name}{(!available ? ", used" : "")}");
            button.Click += (_, _) =>
            {
                if (!available) return;
                selectedSlot = slot; selected = true; chosen.Clear(); Refresh();
            };
            itemFrames.Add((slot, frame));
            return button;
        }

        var inventory = run.Resources.Inventory;
        var heldCards = CampaignTableControls.Items(inventory).Select(entry => entry.Category == FeatureCategory.Tarot
            ? Item(entry.Definition, entry.Slot, true) : CampaignTableControls.StoredItem(entry.Definition)).ToArray();
        var items = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, Spacing = 5,
            Children = { Center(session.PurchasedItemUsed ? "PURCHASE USED" : "PURCHASE · REQUIRED", session.PurchasedItemUsed ? Green : Gold, 11),
                Item(bought, null, !session.PurchasedItemUsed) }
        };
        var offered = new StackPanel { Name = "ShopOfferedCards", Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (card, index) in cards.Select((card, index) => (card, index)))
        {
            var frame = new Border
            {
                Width = 90, Height = 130, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(6), Child = PixelArt.CardContent(card.Face, card.Enhancement, card.Sigil)
            };
            var mark = Center("", Gold, 9); mark.Height = 40;
            var button = new Button
            {
                Name = $"DeckCard{card.Id.Value}", Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand)
            };
            CampaignCardFocus.Apply(button, new StackPanel
            {
                Name = $"PreviewCard{card.Id.Value}", Background = Brushes.Transparent, Spacing = 3,
                // The offered cards drift like the hand at the table; the wrapper keeps the selection pop separate.
                Children = { motion.Float(frame, card.Id.Value), mark }
            });
            AutomationProperties.SetName(button, $"{Card.DisplayRank(card.Face.Rank)} of {card.Face.Suit}, card {card.Id.Value + 1}");
            button.Click += (_, _) =>
            {
                if (!selected || Required() == 0) return;
                if (!chosen.Remove(card.Id))
                {
                    if (Required() == 1) chosen.Clear();
                    if (chosen.Count >= Required()) return;
                    chosen.Add(card.Id);
                    motion.Pulse(frame, 1.07);
                }
                Refresh();
            };
            cardButtons.Add((card, button, frame, mark));
            // Dealt in left to right, each from a slight tilt.
            offered.Children.Add(motion.Enter(button, 120 + index * 55, 0, 26, index % 2 == 0 ? -4 : 4, new BackEaseOut(), 360));
        }
        var done = MenuParts.Button("Done", false, motion, "FinishShopUse", () =>
        {
            if (session.PurchasedItemUsed) actions.Finish();
        }, true, 260);
        done.IsEnabled = session.PurchasedItemUsed;
        foreach (var action in new[] { use, done })
        {
            action.Resources["ButtonBackgroundDisabled"] = MainWindow.Panel;
            action.Resources["ButtonForegroundDisabled"] = Muted;
            action.Resources["ButtonBorderBrushDisabled"] = MainWindow.Line;
        }
        ToolTip.SetTip(done, session.PurchasedItemUsed ? "Return to the shop." : $"Use your purchased {bought.Name} before leaving.");
        var panel = new StackPanel
        {
            Name = "ShopUseSession", Spacing = 10, Margin = new Thickness(24, 18), MaxWidth = 880,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                MenuParts.Title(motion, "SIX OFFERED CARDS", 30),
                Center(session.PurchasedItemUsed ? "Purchase applied. Keep using inventory tickets, or press Done to return to the shop."
                    : "Use your purchase to finish. Inventory tickets can also be used on these same cards.", Muted, 12),
                items, title, description, count, offered,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, Children = { use, done } }
            }
        };
        if (note is not null)
        {
            var report = Center(note, note.EndsWith('!') ? Green : Brush.Parse("#FF617B"), 12);
            report.Name = "CampaignNote";
            panel.Children.Insert(1, report);
        }
        Refresh();
        return CampaignInventoryScreen.Build(MenuParts.Scene(motion, panel, new ScrollViewer
        {
            Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        }), inventory, motion, itemCards: heldCards);
    }

    private static TextBlock Center(string text, IBrush ink, double size)
    {
        var label = CampaignShopScreen.Label(text, ink, size);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        return label;
    }
}
