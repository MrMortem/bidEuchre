using BidEuchre.App;
using BidEuchre.Core;
using BidEuchre.Protocol;

internal static class ArenaTestChild
{
    public static async Task RunAsync(string mode)
    {
        BotPosition? position = null;
        var inner = new BuiltInBotDriver();
        var initialHands = new Dictionary<long, Card[]>();
        while (await Console.In.ReadLineAsync() is { } line)
        {
            if (line == "beuci")
            {
                if (mode == "startup") { await Task.Delay(30000); continue; }
                Console.WriteLine("protocol beuci 1");
                Console.WriteLine("beuciok");
            }
            else if (line == "isready") Console.WriteLine("readyok");
            else if (line.StartsWith("position "))
            {
                position = PositionCodec.Decode(line[9..]);
                if (mode == "history")
                {
                    var game = position.Game;
                    var serial = game.DealSerial!.Value;
                    if (game.Phase == GamePhase.Bidding)
                        initialHands.TryAdd(serial, game.Players[position.Seat].Cards!.ToArray());
                    if (game.Phase is GamePhase.ExchangingBidderCard or GamePhase.ExchangingPartnerCard)
                    {
                        if (!initialHands.TryGetValue(serial, out var initial) || initial.Length != 6 || game.Auction.Count != 4)
                            throw new InvalidOperationException("Exchange skipped private auction history.");
                        if (game.Phase == GamePhase.ExchangingPartnerCard && game.Players[position.Seat].CardCount != 7)
                            throw new InvalidOperationException("Partner did not receive the seventh card.");
                    }
                }
            }
            else if (line == "go")
            {
                if (mode == "info") { while (true) Console.WriteLine("info unlimited"); }
                if (mode == "stderr") { while (true) Console.Error.Write(new string('x', 8192)); }
                if (mode == "exit") { Console.Write("bestaction bi"); return; }
                if (mode == "late") await Task.Delay(1000);
                if (mode == "longline") Console.WriteLine(new string('x', 1024 * 1024 + 1));
                if (mode == "stderr-success")
                {
                    await Console.Error.WriteAsync(new string('x', 256 * 1024));
                    await Console.Error.FlushAsync();
                    Console.WriteLine("bestaction pass");
                }
                else if (mode == "illegal" && position!.Game.Phase == GamePhase.Playing)
                {
                    var illegal = GameRules.CreateDeck().First(card => !position.Game.LegalActions.Cards.Contains(card));
                    Console.WriteLine(BotActionNotation.Format(new BotAction.Play(illegal)));
                }
                else if (mode == "history" && position!.Game.HandNumber == 1 && position.Game.Phase == GamePhase.Bidding)
                {
                    Console.WriteLine(BotActionNotation.Format(position.Game.LegalActions.Bids.Contains(BidLevel.PartnersBest)
                        ? new BotAction.Bid(BidLevel.PartnersBest) : new BotAction.Pass()));
                }
                else Console.WriteLine(BotActionNotation.Format(await inner.ChooseActionAsync(position!.Game, position.Seat)));
            }
            else if (line == "quit") return;
            await Console.Out.FlushAsync();
        }
    }
}
