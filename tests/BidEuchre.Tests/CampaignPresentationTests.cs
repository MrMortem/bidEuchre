using BidEuchre.Core;
using BidEuchre.App;
internal static class CampaignPresentationTests
{
    public static void PhysicalMotionKeys()
    {
        var game = new GameEngine(["You", "W", "P", "E"], 1, campaign: new CampaignContext(CampaignSnapshot.Create(1))); game.StartGame(3);
        var cv = game.CreateCampaignView(0); var face = Card.Parse("9C");
        CampaignTrickPlay Play(int seat, int id) => new(seat, new(new(id), face, null, null), Suit.Clubs);
        var first = game.CreateView(0) with { Phase = GamePhase.Playing, CurrentTrick = [new(0, face)], Campaign = cv with { Trick = [Play(0, 0)] } };
        var tracker = new TableMotionTracker(); tracker.Update(first, "test");
        var second = first with { CurrentTrick = [new(0, face), new(1, face)], Campaign = cv with { Trick = [Play(0, 0), Play(1, 1)] } };
        var cue = tracker.Update(second, "test"); Assert.True(cue.AddedPlays.Contains("1:id1"));
    }
}
