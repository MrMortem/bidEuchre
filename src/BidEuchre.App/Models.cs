using BidEuchre.Core;

namespace BidEuchre.App;

public enum PlayerKind
{
    Human,
    Bot
}

public sealed record SeatConfiguration(int Seat, string Name, PlayerKind Kind, string? EngineId);

public sealed record EngineDescriptor(
    string Id,
    string Name,
    string Author,
    bool IsBuiltIn,
    string? Executable,
    string? Arguments);

public sealed record CreateSessionRequest(
    string? Name,
    IReadOnlyList<CreateSeatRequest>? Seats,
    int? Seed,
    int? BotDelayMilliseconds = null);

public sealed record CreateSeatRequest(string? Name, PlayerKind Kind, string? EngineId);

public sealed record LoadEngineRequest(string? Executable, string? Arguments);

public sealed record GameActionRequest(
    int Seat,
    string? Type,
    BidLevel? Bid,
    ContractMode? Mode,
    Suit? Suit,
    string? Card);

public sealed record SessionSummary(
    string Id,
    string Name,
    bool Started,
    GamePhase Phase,
    int HandNumber,
    int[] Scores,
    IReadOnlyList<SeatConfiguration> Seats);

public sealed record SessionState(
    string Id,
    string Name,
    bool Started,
    IReadOnlyList<SeatConfiguration> Seats,
    GameView? Game,
    string? Error);
