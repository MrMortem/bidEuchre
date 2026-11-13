using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BidEuchre.Core;
namespace BidEuchre.Desktop;
internal static class CampaignTableControls
{
    public static Control Build(CampaignGameView view,Func<CampaignCommand,Task> send)
    {
        var panel=new StackPanel{Spacing=6};
        panel.Children.Add(Label($"${view.Money} · HAND {view.HandNumber}{(view.HandNumber>8?" · OVERTIME":" OF 8")} · ±{view.Threshold}"));
        panel.Children.Add(Label("Jokers: "+(view.Inventory.Jokers.Length==0?"none":string.Join(", ",view.Inventory.Jokers.Select(j=>FeatureCatalog.Get(FeatureCategory.Joker,j.Id.ToString()).Name)))));
        if(view.PendingChoice is not null)panel.Children.Add(Label(view.PendingChoice));
        foreach(var command in view.LegalActions)
        {
            var button=new Button{Content=Describe(command,view),HorizontalAlignment=HorizontalAlignment.Stretch};
            button.Click+=async(_,_)=>await send(command);panel.Children.Add(button);
        }
        var bidding=view.LegalActions.Any(c=>c is CampaignCommand.Bid or CampaignCommand.Pass or CampaignCommand.PanicButton);
        if(view.ViewerSeat==0 && bidding && view.PendingChoice is null)
            for(var slot=0;slot<view.Inventory.Tarot.Length;slot++)
            {
                var index=slot;var id=view.Inventory.Tarot[slot];var definition=FeatureCatalog.Get(FeatureCategory.Tarot,id.ToString());
                var selected=new List<CardId>();var required=id==TarotId.DevilishDeal?3:id is TarotId.Gambler or TarotId.Astronomer or TarotId.TaxCollector?0:1;
                var body=new StackPanel{Spacing=4};body.Children.Add(Label(definition.Description));
                var use=new Button{Content="Use "+definition.Name,IsEnabled=required==0};
                if(required>0)
                    foreach(var card in view.Players[0].VisibleCards)
                    {
                        var choice=new CheckBox{Content=$"{card.Face.Code} · #{card.Id.Value+1}",Foreground=MainWindow.Ink};
                        choice.IsCheckedChanged+=(_,_)=>{if(choice.IsChecked==true)selected.Add(card.Id);else selected.Remove(card.Id);use.IsEnabled=selected.Count==required;};body.Children.Add(choice);
                    }
                use.Click+=async(_,_)=>await send(new CampaignCommand.UseTarot(index,selected.ToArray()));body.Children.Add(use);
                panel.Children.Add(new Expander{Header="Tarot: "+definition.Name,Content=body});
            }
        if(view.LegalActions.Count==0)panel.Children.Add(Label("Waiting for the next player."));
        return panel;
    }
    private static TextBlock Label(string text)=>new(){Text=text,Foreground=MainWindow.Ink,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3)};
    private static string Card(CardId id,CampaignGameView view)
    {var card=view.Players.SelectMany(p=>p.VisibleCards).FirstOrDefault(c=>c.Id==id);return card is null?$"card #{id.Value+1}":$"{card.Face.Code} · #{id.Value+1}";}
    public static string Describe(CampaignCommand command,CampaignGameView view)=>command switch
    {
        CampaignCommand.Bid b=>$"Bid {(b.Level<=BidLevel.Six?((int)b.Level).ToString():b.Level.ToString())}{(b.Variant is {} v?" · "+v:"")}",
        CampaignCommand.Pass=>"Pass",
        CampaignCommand.PanicButton=>"Panic Button · −8 points · consumes this hand",
        CampaignCommand.ChooseContract c=>c.Trump is {} suit?$"Trump · {suit}":c.Mode.ToString(),
        CampaignCommand.Play p=>$"Play {Card(p.Id,view)}{(p.EffectiveSuit is {} suit?" as "+suit:"")}",
        CampaignCommand.Exchange e=>"Exchange "+Card(e.Id,view),
        CampaignCommand.SwapPartnerCard s=>"Swap "+Card(s.Id,view),
        CampaignCommand.ChooseRevealSeat r=>$"Reveal a card from seat {r.Seat+1}",
        CampaignCommand.RevealPartnerCard r=>"Reveal "+Card(r.Id,view),
        CampaignCommand.SelectOpponent s=>$"Duel seat {s.Seat+1}",
        _=>command.GetType().Name
    };
}
