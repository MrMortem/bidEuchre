using BidEuchre.Core;
using BidEuchre.Protocol;

namespace BidEuchre.App;

public interface IBotDriver : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task NewGameAsync(CancellationToken cancellationToken = default);
    Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken = default);
    Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class ProcessBotDriver(EngineDescriptor descriptor) : IBotDriver
{
    private readonly EngineProcessClient _client = new(
        descriptor.Executable ?? throw new ArgumentException("External engine has no executable."),
        descriptor.Arguments);

    public Task StartAsync(CancellationToken cancellationToken = default) => _client.StartAsync(cancellationToken);

    public Task NewGameAsync(CancellationToken cancellationToken = default) => _client.NewGameAsync(cancellationToken);

    public Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken = default) =>
        _client.ChooseActionAsync(view, seat, cancellationToken);

    public Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken = default) =>
        _client.ObserveAsync(view, seat, cancellationToken);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

public sealed class BuiltInBotDriver : IBotDriver
{
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NewGameAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ObserveAsync(GameView view, int seat, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<BotAction> ChooseActionAsync(GameView view, int seat, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var legal = view.LegalActions;

        BotAction action = view.Phase switch
        {
            GamePhase.Bidding when !legal.CanPass => new BotAction.Bid(legal.Bids[0]),
            GamePhase.Bidding => ChooseBid(view),
            GamePhase.ChoosingContract => ChooseContract(view),
            GamePhase.ExchangingBidderCard => new BotAction.Exchange(legal.Cards.MinBy(CardStrength)),
            GamePhase.ExchangingPartnerCard => new BotAction.Exchange(legal.Cards.MaxBy(CardStrength)),
            GamePhase.Playing when view.Contract?.Mode is ContractMode.Low =>
                new BotAction.Play(legal.Cards.MinBy(CardStrength)),
            GamePhase.Playing => new BotAction.Play(legal.Cards.MaxBy(CardStrength)),
            _ => throw new ProtocolException($"TableBot cannot act during {view.Phase}.")
        };

        return Task.FromResult(action);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static BotAction ChooseBid(GameView view)
    {
        var hand = view.Players.Single(player => player.Seat == view.CurrentSeat).Cards!;
        var strongestSuit = Enum.GetValues<Suit>()
            .Max(suit => hand.Count(card => card.Suit == suit || card.Rank is Rank.Jack));
        if (view.HighBid is null && strongestSuit >= 3 && view.LegalActions.Bids.Contains(BidLevel.Three))
        {
            return new BotAction.Bid(BidLevel.Three);
        }

        return new BotAction.Pass();
    }

    private static BotAction ChooseContract(GameView view)
    {
        var hand = view.Players.Single(player => player.Seat == view.CurrentSeat).Cards!;
        var suit = Enum.GetValues<Suit>().MaxBy(candidate => hand.Sum(card => TrumpScore(card, candidate)));
        return new BotAction.ChooseContract(ContractMode.Trump, suit);
    }

    private static int TrumpScore(Card card, Suit suit)
    {
        if (card.Rank is Rank.Jack && card.Suit == suit)
        {
            return 20;
        }

        var sameColor = card.Suit is Suit.Hearts or Suit.Diamonds
            ? suit is Suit.Hearts or Suit.Diamonds
            : suit is Suit.Clubs or Suit.Spades;
        if (card.Rank is Rank.Jack && sameColor)
        {
            return 18;
        }

        return card.Suit == suit ? (int)card.Rank : card.Rank is Rank.Ace ? 3 : 0;
    }

    private static int CardStrength(Card card) => (int)card.Rank;
}
