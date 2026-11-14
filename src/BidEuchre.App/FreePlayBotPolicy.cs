using BidEuchre.Core;
using BidEuchre.Protocol;

namespace BidEuchre.App;

/// <summary>A deterministic free-play heuristic using only this seat's cards and public table information.</summary>
public static class FreePlayBotPolicy
{
    private static readonly IReadOnlyList<Card> Deck = GameRules.CreateDeck();
    private const double PartnerTricks = .75;

    public static BotAction Choose(GameView view, int seat)
    {
        GameRules.ValidateSeat(seat);
        var hand = view.Players.Single(player => player.Seat == seat).Cards
            ?? throw new ProtocolException("TableBot needs its own visible hand.");
        return view.Phase switch
        {
            GamePhase.Bidding => ChooseBid(view, hand),
            GamePhase.ChoosingContract => ChooseContract(view, hand),
            GamePhase.ExchangingBidderCard => new BotAction.Exchange(OrderCards(view.LegalActions.Cards, view.Contract!).First()),
            GamePhase.ExchangingPartnerCard => new BotAction.Exchange(OrderCards(view.LegalActions.Cards, view.Contract!).Last()),
            GamePhase.Playing => new BotAction.Play(ChoosePlay(view, seat)),
            _ => throw new ProtocolException($"TableBot cannot act during {view.Phase}.")
        };
    }

    private static BotAction ChooseBid(GameView view, IReadOnlyList<Card> hand)
    {
        foreach (var bid in view.LegalActions.Bids.OrderDescending())
        {
            var best = Contracts(bid).Select(contract => Evaluate(hand, contract)).Max();
            if (bid == BidLevel.Alone)
            {
                // Never gamble an Alone on partner help: all six cards must be leading controls.
                if (Contracts(bid).Any(contract => Controls(hand, contract) == 6)) return new BotAction.Bid(bid);
            }
            else if (bid == BidLevel.PartnersBest)
            {
                // Five top trumps leave only one card for the partner to improve.
                if (Contracts(bid).Any(contract => TopTrumpRun(hand, contract) >= 5)) return new BotAction.Bid(bid);
            }
            else if (best + PartnerTricks >= (int)bid)
            {
                return new BotAction.Bid(bid);
            }
        }
        if (view.LegalActions.CanPass) return new BotAction.Pass();
        if (view.LegalActions.Bids.Count > 0) return new BotAction.Bid(view.LegalActions.Bids.Min());
        throw new ProtocolException("TableBot has no legal bid or pass.");
    }

    private static BotAction ChooseContract(GameView view, IReadOnlyList<Card> hand)
    {
        var choices = view.LegalActions.ContractModes.SelectMany(mode => mode == ContractMode.Trump
            ? view.LegalActions.TrumpSuits.Select(suit => new Contract(view.HighBid!.Value, mode, suit))
            : [new Contract(view.HighBid!.Value, mode, null)]);
        var contract = choices.OrderByDescending(candidate =>
                candidate.IsAlone ? Controls(hand, candidate) : candidate.IsPartnersBest
                    ? TopTrumpRun(hand, candidate) : Evaluate(hand, candidate))
            .ThenBy(candidate => candidate.Mode).ThenBy(candidate => candidate.Trump).First();
        return new BotAction.ChooseContract(contract.Mode, contract.Trump);
    }

    private static IEnumerable<Contract> Contracts(BidLevel bid)
    {
        foreach (var suit in Enum.GetValues<Suit>()) yield return new(bid, ContractMode.Trump, suit);
        if (bid is BidLevel.Three or BidLevel.PartnersBest or BidLevel.Two) yield break;
        yield return new(bid, ContractMode.High, null);
        yield return new(bid, ContractMode.Low, null);
    }

    // Inspired by the C++ engine's control heuristic: top runs, secondary controls and trump length.
    // This estimates tricks, rather than reusing the C++ engine's differently scaled bidding weights.
    private static double Evaluate(IReadOnlyList<Card> hand, Contract contract)
    {
        if (contract.Mode != ContractMode.Trump)
        {
            return Enum.GetValues<Suit>().Sum(suit =>
            {
                var ordered = SuitCards(suit, contract);
                var run = ordered.TakeWhile(hand.Contains).Count();
                return run + ordered.Skip(run + 1).Where(hand.Contains).Sum(card =>
                    card == ordered[1] ? .30 : card == ordered[2] ? .12 : 0);
            });
        }

        var trump = contract.Trump!.Value;
        var trumps = hand.Where(card => GameRules.EffectiveSuit(card, contract) == trump).ToArray();
        var topRun = TopTrumpRun(hand, contract);
        var ladder = SuitCards(trump, contract);
        var tricks = trumps.Sum(card => ladder.IndexOf(card) < topRun ? 1 :
            card.Rank switch { Rank.Jack => .85, Rank.Ace => .75, Rank.King => .50, Rank.Queen => .34, _ => .20 });
        tricks += hand.Count(card => GameRules.EffectiveSuit(card, contract) != trump && card.Rank == Rank.Ace) * .60;
        if (trumps.Length >= 3)
        {
            tricks += .15 * (trumps.Length - 2);
            tricks += Enum.GetValues<Suit>().Count(suit => suit != trump &&
                !hand.Any(card => GameRules.EffectiveSuit(card, contract) == suit)) * .15;
        }
        return Math.Min(hand.Count, tricks);
    }

    private static int Controls(IReadOnlyList<Card> hand, Contract contract) => contract.Mode == ContractMode.Trump
        // An off-suit Ace can be trumped when playing alone, so only count a solid trump run.
        ? TopTrumpRun(hand, contract)
        : Enum.GetValues<Suit>().Sum(suit => SuitCards(suit, contract).TakeWhile(hand.Contains).Count());

    private static int TopTrumpRun(IReadOnlyList<Card> hand, Contract contract) =>
        SuitCards(contract.Trump!.Value, contract).TakeWhile(hand.Contains).Count();

    // Let Core order the bowers and the Low ranks instead of maintaining a second rule implementation.
    private static List<Card> SuitCards(Suit suit, Contract contract) => Deck
        .Where(card => GameRules.EffectiveSuit(card, contract) == suit)
        .OrderByDescending(card => Power(card, contract)).ToList();

    private static int Power(Card card, Contract contract)
    {
        var suit = GameRules.EffectiveSuit(card, contract);
        // Compare side-suit ranks on one scale: a suit shortened by the left bower still has a valuable Ace.
        if (contract.Mode == ContractMode.Trump && suit != contract.Trump) return (int)card.Rank - (int)Rank.Nine;
        var beaten = Deck.Count(other => other != card && GameRules.EffectiveSuit(other, contract) == suit &&
            GameRules.DetermineTrickWinner([new(0, card), new(1, other)], contract) == 0);
        return beaten + (contract.Mode == ContractMode.Trump && suit == contract.Trump ? 100 : 0);
    }

    private static IOrderedEnumerable<Card> OrderCards(IEnumerable<Card> cards, Contract contract) =>
        cards.OrderBy(card => Power(card, contract)).ThenBy(card => card.Suit).ThenBy(card => card.Rank);

    private static Card ChoosePlay(GameView view, int seat)
    {
        var contract = view.Contract!;
        var cheap = OrderCards(view.LegalActions.Cards, contract).ToArray();
        if (view.CurrentTrick.Count == 0) return cheap[^1];

        var winningSeat = GameRules.DetermineTrickWinner(view.CurrentTrick, contract);
        if (GameRules.TeamForSeat(winningSeat) == GameRules.TeamForSeat(seat)) return cheap[0];
        // Test complete trick comparisons, including trump and following suit, against the engine itself.
        foreach (var card in cheap)
            if (GameRules.DetermineTrickWinner([.. view.CurrentTrick, new(seat, card)], contract) == seat) return card;
        return cheap[0];
    }
}
