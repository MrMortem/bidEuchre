using BidEuchre.Core;
using BidEuchre.Protocol;

namespace BidEuchre.Arena;

public static class ActionApplier
{
    public static void Apply(GameEngine game, int seat, BotAction action)
    {
        int id;
        try { id = FixtureExporter.ActionId(action); }
        catch (ProtocolException exception) { throw new GameRuleException(exception.Message); }
        if (game.CurrentSeat != seat || !FixtureExporter.LegalActionIds(game.CreateView(seat)).Contains(id))
            throw new GameRuleException("Action is not legal for the acting seat.");
        switch (action)
        {
            case BotAction.Pass: game.PlaceBid(seat, null); break;
            case BotAction.Bid bid: game.PlaceBid(seat, bid.Level); break;
            case BotAction.ChooseContract contract: game.ChooseContract(seat, contract.Mode, contract.Trump); break;
            case BotAction.Exchange exchange: game.ExchangeCard(seat, exchange.Card); break;
            case BotAction.Play play: game.PlayCard(seat, play.Card); break;
            default: throw new ProtocolException("Unknown arena action.");
        }
    }
}
