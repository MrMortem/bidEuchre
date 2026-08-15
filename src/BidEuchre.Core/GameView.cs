namespace BidEuchre.Core;

public sealed record PlayerView(int Seat, string Name, int Team, int CardCount, IReadOnlyList<Card>? Cards, bool IsSittingOut);

public sealed record LegalActionView(
    bool CanPass,
    IReadOnlyList<BidLevel> Bids,
    IReadOnlyList<ContractMode> ContractModes,
    IReadOnlyList<Suit> TrumpSuits,
    IReadOnlyList<Card> Cards);

public sealed record GameView(
    GamePhase Phase,
    int HandNumber,
    int Dealer,
    int? CurrentSeat,
    int[] Scores,
    IReadOnlyList<PlayerView> Players,
    IReadOnlyList<AuctionAction> Auction,
    BidLevel? HighBid,
    int? Bidder,
    Contract? Contract,
    IReadOnlyList<CardPlay> CurrentTrick,
    IReadOnlyList<CompletedTrick> CompletedTricks,
    int[] TricksByTeam,
    int? GameWinner,
    LegalActionView LegalActions,
    IReadOnlyList<string> Events);
