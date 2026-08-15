using BidEuchre.Core;
using BidEuchre.Protocol;

namespace BidEuchre.SampleBot;

public sealed class SimpleBot : IBidEuchreBot
{
    public string Name => "TableBot";
    public string Author => "Bid Euchre Project";

    public ValueTask<BotAction> ChooseActionAsync(
        BotPosition position,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var view = position.Game;
        var hand = view.Players.Single(player => player.Seat == position.Seat).Cards
            ?? throw new ProtocolException("The engine position did not include this bot's hand.");

        BotAction action = view.Phase switch
        {
            GamePhase.Bidding => ChooseBid(view, hand),
            GamePhase.ChoosingContract => ChooseContract(view, hand),
            GamePhase.ExchangingBidderCard => new BotAction.Exchange(
                view.LegalActions.Cards.MinBy(card => CardPower(card, view.Contract))!),
            GamePhase.ExchangingPartnerCard => new BotAction.Exchange(
                view.LegalActions.Cards.MaxBy(card => CardPower(card, view.Contract))!),
            GamePhase.Playing => ChoosePlay(view),
            _ => throw new ProtocolException($"No bot action is available during {view.Phase}.")
        };

        return ValueTask.FromResult(action);
    }

    private static BotAction ChooseBid(GameView view, IReadOnlyList<Card> hand)
    {
        if (view.LegalActions.Bids.Count is 0)
        {
            return new BotAction.Pass();
        }

        var bestTrump = Enum.GetValues<Suit>().Max(suit => TrumpHandScore(hand, suit));
        var highScore = hand.Sum(card => card.Rank is Rank.Ace ? 4 : card.Rank is Rank.King ? 2 : 0);
        var lowScore = hand.Sum(card => card.Rank is Rank.Nine ? 4 : card.Rank is Rank.Ten ? 2 : 0);
        var estimate = Math.Clamp(2 + Math.Max(bestTrump / 14, Math.Max(highScore, lowScore) / 5), 3, 6);
        var desired = (BidLevel)estimate;

        if (view.LegalActions.Bids.Contains(desired) &&
            (bestTrump >= 18 || highScore >= 9 || lowScore >= 9 || !view.LegalActions.CanPass))
        {
            return new BotAction.Bid(desired);
        }

        if (!view.LegalActions.CanPass)
        {
            return new BotAction.Bid(view.LegalActions.Bids[0]);
        }

        return new BotAction.Pass();
    }

    private static BotAction ChooseContract(GameView view, IReadOnlyList<Card> hand)
    {
        var trump = Enum.GetValues<Suit>().MaxBy(suit => TrumpHandScore(hand, suit));
        if (view.LegalActions.ContractModes.Count is 1 || view.HighBid is BidLevel.PartnersBest)
        {
            return new BotAction.ChooseContract(ContractMode.Trump, trump);
        }

        var trumpScore = TrumpHandScore(hand, trump);
        var highScore = hand.Sum(card => (int)card.Rank);
        var lowScore = hand.Sum(card => 23 - (int)card.Rank);

        if (trumpScore >= highScore && trumpScore >= lowScore)
        {
            return new BotAction.ChooseContract(ContractMode.Trump, trump);
        }

        return highScore >= lowScore
            ? new BotAction.ChooseContract(ContractMode.High, null)
            : new BotAction.ChooseContract(ContractMode.Low, null);
    }

    private static BotAction ChoosePlay(GameView view)
    {
        var legal = view.LegalActions.Cards;
        if (legal.Count is 0)
        {
            throw new ProtocolException("The position contains no legal card play.");
        }

        var card = view.Contract?.Mode is ContractMode.Low
            ? legal.MinBy(item => CardPower(item, view.Contract))
            : legal.MaxBy(item => CardPower(item, view.Contract));
        return new BotAction.Play(card!);
    }

    private static int TrumpHandScore(IReadOnlyList<Card> hand, Suit trump) =>
        hand.Sum(card =>
        {
            var contract = Contract.Create(BidLevel.Three, ContractMode.Trump, trump);
            if (GameRules.EffectiveSuit(card, contract) != trump)
            {
                return card.Rank is Rank.Ace ? 3 : 0;
            }

            if (card.Rank is Rank.Jack && card.Suit == trump)
            {
                return 14;
            }

            if (card.Rank is Rank.Jack)
            {
                return 12;
            }

            return (int)card.Rank - 5;
        });

    private static int CardPower(Card card, Contract? contract)
    {
        if (contract?.Mode is ContractMode.Trump && GameRules.EffectiveSuit(card, contract) == contract.Trump)
        {
            if (card.Rank is Rank.Jack && card.Suit == contract.Trump)
            {
                return 100;
            }

            if (card.Rank is Rank.Jack)
            {
                return 99;
            }

            return 50 + (int)card.Rank;
        }

        return (int)card.Rank;
    }
}
