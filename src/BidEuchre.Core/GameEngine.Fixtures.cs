namespace BidEuchre.Core;

internal sealed record FixtureStart(
    string[] Names,
    Card[][] Hands,
    int Dealer,
    int[] Scores,
    int HandNumber = 1,
    long DealSerial = 1, CampaignSnapshot? Campaign = null, MatchOptions? Options = null, CardOverlay[]? CampaignOverlays = null);

public sealed partial class GameEngine
{
    internal static GameEngine CreateFixture(FixtureStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(start.Names);
        ArgumentNullException.ThrowIfNull(start.Hands);
        ArgumentNullException.ThrowIfNull(start.Scores);
        if (start.Hands.Length != 4 || start.Hands.Any(hand => hand is null || hand.Length != 6))
        {
            throw new ArgumentException("Exactly four hands of six cards are required.", nameof(start));
        }

        var cards = start.Hands.SelectMany(hand => hand).ToArray();
        if (cards.Any(card => !Enum.IsDefined(card.Suit) || !Enum.IsDefined(card.Rank)) ||
            cards.Distinct().Count() != 24)
        {
            throw new ArgumentException("The hands must contain all 24 unique valid cards.", nameof(start));
        }

        if (start.Dealer is < 0 or > 3)
        {
            throw new ArgumentException("The dealer must be a seat from 0 through 3.", nameof(start));
        }

        if (start.Scores.Length != 2)
        {
            throw new ArgumentException("Exactly two team scores are required.", nameof(start));
        }

        if (start.HandNumber < 1 || start.DealSerial < 1)
        {
            throw new ArgumentException("Hand number and deal serial must be positive.", nameof(start));
        }

        var game = new GameEngine(start.Names, options: start.Options, campaign: start.Campaign is null ? null : new CampaignContext(start.Campaign))
        {
            Dealer = start.Dealer,
            HandNumber = start.HandNumber,
            DealSerial = start.DealSerial,
            Phase = GamePhase.Bidding,
            CurrentSeat = GameRules.NextSeat(start.Dealer)
        };
        if (start.CampaignOverlays is not null)
            foreach (var overlay in start.CampaignOverlays) game._overlays.Add(overlay.Id, overlay);
        start.Scores.CopyTo(game._scores, 0);
        if (game.DetermineGameWinner() is not null)
        {
            throw new ArgumentException("A bidding fixture cannot begin after the game has been won.", nameof(start));
        }

        for (var seat = 0; seat < 4; seat++)
        {
            game._hands[seat].AddRange(start.Hands[seat]);
        }

        game._events.Add($"Hand {game.HandNumber} began. {game._playerNames[game.Dealer]} is the dealer.");
        return game;
    }
}
