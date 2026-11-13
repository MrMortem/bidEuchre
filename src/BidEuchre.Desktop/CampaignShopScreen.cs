using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.App;
using BidEuchre.Core;
namespace BidEuchre.Desktop;

internal static class CampaignShopScreen
{
    private static TextBlock Text(string text,double size=14)=>new(){Text=text,FontSize=size,Foreground=MainWindow.Ink,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4)};
    public static Control Build(CampaignRun run,Action<string> buy,Action<JokerId> sell,Action<int> discard,Action next,PixelMotion motion,string? note=null)
    {
        var panel=new StackPanel{Spacing=10,Width=740,Margin=new Thickness(24)};
        panel.Children.Add(Text($"SHOP · ${run.Money}",28));
        if(note is not null)panel.Children.Add(Text(note));
        foreach(var offer in run.Shop!.Offers)
        {
            var definition=FeatureCatalog.Get(offer.Category,offer.FeatureId);
            var box=new StackPanel{Spacing=3};
            box.Children.Add(Text($"{definition.Name} · {offer.Category} · ${offer.Price}",18));
            box.Children.Add(Text(definition.Description));
            var button=MenuParts.Button(offer.Sold?"Sold":"Buy",false,motion,$"Buy{offer.Id}",()=>buy(offer.Id),true);
            var inventory=run.Resources.Inventory;
            button.IsEnabled=!offer.Sold&&run.Money>=offer.Price
                &&(offer.Category!=FeatureCategory.Joker||inventory.Jokers.Length<5)
                &&(offer.Category!=FeatureCategory.Tarot||inventory.Tarot.Length<2);
            box.Children.Add(button);panel.Children.Add(box);
        }
        if(run.Resources.Inventory.Unlocks.Length==8)panel.Children.Add(Text("All Planet bidding options unlocked."));
        panel.Children.Add(Text($"JOKERS {run.Resources.Inventory.Jokers.Length}/5",18));
        foreach(var joker in run.Resources.Inventory.Jokers)
        {var definition=FeatureCatalog.Get(FeatureCategory.Joker,joker.Id.ToString());panel.Children.Add(Text(definition.Name+": "+definition.Description));panel.Children.Add(MenuParts.Button("Sell for $2",false,motion,$"Sell{joker.Id}",()=>sell(joker.Id),true));}
        panel.Children.Add(Text($"TAROT {run.Resources.Inventory.Tarot.Length}/2",18));
        for(var i=0;i<run.Resources.Inventory.Tarot.Length;i++)
        {var slot=i;var tarot=run.Resources.Inventory.Tarot[i];panel.Children.Add(MenuParts.Button($"Discard {FeatureCatalog.Get(FeatureCategory.Tarot,tarot.ToString()).Name}",false,motion,$"DiscardTarot{i}",()=>discard(slot),true));}
        panel.Children.Add(MenuParts.Button("Inspect shared deck",false,motion,"InspectDeck",()=>buy("inspect"),true));
        panel.Children.Add(MenuParts.Button("Next Match",true,motion,"CampaignNextMatch",next));
        return MenuParts.Scene(motion,new ScrollViewer{MaxHeight=710,Content=panel});
    }
}
internal static class CampaignDeckPicker
{
    public static Control Build(IReadOnlyList<CardState> deck,Action<CardId> select,Action cancel,PixelMotion motion,string title="Choose a card for this Sigil")
    {
        var panel=new StackPanel{Width=740,Spacing=10,Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock{Text=title,Foreground=MainWindow.Ink,FontSize=22,TextWrapping=TextWrapping.Wrap});
        var cards=new WrapPanel{Orientation=Orientation.Horizontal};
        foreach(var card in deck)
        {
            var label=$"{card.Face.Code} · #{card.Id.Value+1}\n{card.Enhancement?.ToString()??"No enhancement"}\n{card.Sigil?.ToString()??"No Sigil"}";
            var button=new Button{Content=label,Width=170,MinHeight=90,Margin=new Thickness(4)};
            button.Click+=(_,_)=>select(card.Id);cards.Children.Add(button);
        }
        panel.Children.Add(cards);panel.Children.Add(MenuParts.Button("Back",false,motion,"CancelDeckChoice",cancel,true));
        return MenuParts.Scene(motion,new ScrollViewer{MaxHeight=710,Content=panel});
    }
}
