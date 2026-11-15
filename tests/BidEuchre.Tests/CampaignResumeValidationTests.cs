using System.Text.Json.Nodes;
using BidEuchre.App;
using BidEuchre.Core;

internal static class CampaignResumeValidationTests
{
    private static GameEngine CardFixture()
    {
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var resources = CampaignSnapshot.Create(seed) with
            { Inventory = new([new(JokerId.Cheater), new(JokerId.StickyFingers)], [], [GumballId.PartnersBest]) };
            for (var i = 0; i < resources.Deck.Length; i++) resources.Deck[i] = resources.Deck[i] with { Enhancement = EnhancementId.Wild };
            resources.Deck[0] = resources.Deck[0] with { Face = new(Suit.Hearts, Rank.Jack) };
            var game = new GameEngine(["You", "West", "Partner", "East"], 42,
                new MatchOptions(10000) { HandLimit = 3 }, new(resources));
            game.StartGame(3);
            if (!game.CreateCampaignView(0).Players[0].VisibleCards.Any(c => c.Id.Value == 0)) continue;
            game.ExecuteCampaignAction(0, new CampaignCommand.ChooseRevealSeat(2));
            return game;
        }
        throw new Exception("Fixture requires physical card zero in the human hand.");
    }

    private static void Auction(GameEngine game, BidLevel bid, GumballId? variant = null)
    {
        game.ExecuteCampaignAction(0, new CampaignCommand.Bid(bid, variant));
        while (game.Phase == GamePhase.Bidding) game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.Pass());
    }

    public static void NestedCardMembersMustBePresent()
    {
        var game = CardFixture(); var auction = game.ExportCampaignMatchSnapshot();
        Auction(game, BidLevel.Three); game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts));
        game.ExecuteCampaignAction(0, new CampaignCommand.Play(new(0), Suit.Hearts));
        var partial = game.ExportCampaignMatchSnapshot();
        while (game.CompletedTricks.Count == 0)
        { var seat = game.CampaignActor!.Value; game.ExecuteCampaignAction(seat, game.CreateCampaignView(seat).LegalActions[0]); }
        var complete = game.ExportCampaignMatchSnapshot();
        Assert.Equal(1, complete.Overlays.Length);
        var exchangeGame = CardFixture(); Auction(exchangeGame, BidLevel.PartnersBest);
        exchangeGame.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts));
        exchangeGame.ExecuteCampaignAction(0, new CampaignCommand.Exchange(new(0)));
        var exchange = exchangeGame.ExportCampaignMatchSnapshot();
        var zeroIndex = Array.FindIndex(auction.Hands[0], id => id.Value == 0);
        var cases = new List<(string Label, CampaignMatchSnapshot Snapshot, string[] Paths)>
        {
            ("resource face suit", complete, ["Run/Resources/Deck/6/Face/Suit", "Run/LiveMatch/Resources/Deck/6/Face/Suit"]),
            ("resource face rank", complete, ["Run/Resources/Deck/6/Face/Rank", "Run/LiveMatch/Resources/Deck/6/Face/Rank"]),
            ("resource identity", complete, ["Run/Resources/Deck/0/Id/Value", "Run/LiveMatch/Resources/Deck/0/Id/Value"]),
            ("hand identity", auction, [$"Run/LiveMatch/Hands/0/{zeroIndex}/Value"]),
            ("current play identity", partial, ["Run/LiveMatch/CurrentTrick/0/Id/Value"]),
            ("completed play identity", complete, ["Run/LiveMatch/CompletedTricks/0/Plays/0/Id/Value"]),
            ("historical card identity", complete, ["Run/LiveMatch/TrickHistory/0/Plays/0/Card/Id/Value"]),
            ("historical face suit", complete, ["Run/LiveMatch/TrickHistory/0/Plays/0/Card/Face/Suit"]),
            ("historical face rank", complete, ["Run/LiveMatch/TrickHistory/0/Plays/0/Card/Face/Rank"]),
            ("overlay identity", complete, ["Run/LiveMatch/Overlays/0/Id/Value"]),
            ("overlay face suit", complete, ["Run/LiveMatch/Overlays/0/Face/Suit"]),
            ("overlay face rank", complete, ["Run/LiveMatch/Overlays/0/Face/Rank"]),
            ("wild declaration identity", partial, ["Run/LiveMatch/WildSuits/0/Id/Value"]),
            ("reveal identity", complete, ["Run/LiveMatch/Reveals/0/Id/Value"]),
            ("last match identity", partial, ["Run/LiveMatch/LastMatchCard/Value"]),
            ("pending exchange identity", exchange, ["Run/LiveMatch/PendingExchangeCard/Value"])
        };
        var path = Path.GetTempFileName(); var accepted = new List<string>();
        try
        {
            foreach (var test in cases)
            {
                var run = new CampaignRun(Phase: CampaignPhase.InMatch).WithCheckpoint(new(test.Snapshot));
                Assert.True(CampaignStore.Save(run, path), test.Label + " baseline must save");
                Assert.Equal(CampaignLoadStatus.Loaded, CampaignStore.LoadSave(path).Status, test.Label + " baseline must load");
                var node = JsonNode.Parse(File.ReadAllText(path))!;
                foreach (var member in test.Paths) Remove(node, member);
                var damaged = node.ToJsonString(); File.WriteAllText(path, damaged);
                if (CampaignStore.LoadSave(path).Status != CampaignLoadStatus.Corrupt) accepted.Add(test.Label);
                Assert.Equal(damaged, File.ReadAllText(path), "Rejected input must remain untouched.");
            }
            Assert.Equal("", string.Join(", ", accepted), "Missing struct members must not silently default. Accepted: " + string.Join(", ", accepted));
            // These resource-only defaults predate live snapshots and remain valid in v2.
            Assert.True(CampaignStore.Save(new CampaignRun(Lives: 2, Money: 9), path));
            var legacy = JsonNode.Parse(File.ReadAllText(path))!; legacy["Version"] = 2;
            Remove(legacy, "Run/Resources/Deck/0/Enhancement"); Remove(legacy, "Run/Resources/Deck/0/Sigil");
            Remove(legacy, "Run/Resources/Inventory/Sigils"); Remove(legacy, "Run/Resources/Inventory/GumballExtraCopies");
            Remove(legacy, "Run/Resources/GeneratedTarot"); Remove(legacy, "Run/Resources/PreviousContract");
            File.WriteAllText(path, legacy.ToJsonString());
            var loaded = CampaignStore.LoadSave(path); Assert.Equal(CampaignLoadStatus.Loaded, loaded.Status);
            Assert.Equal(2, loaded.Save!.Run.Lives); Assert.Equal(9, loaded.Save.Run.Money);
            Assert.Equal(4, loaded.Save.Run.Resources.GeneratedTarot.Length);
            Assert.Equal(0, loaded.Save.Run.Resources.Inventory.Sigils.Length);
            Assert.Equal(0, loaded.Save.Run.Resources.Inventory.GumballExtraCopies.Count);
            Assert.True(loaded.Save.Run.Resources.Deck[0].Enhancement is null && loaded.Save.Run.Resources.Deck[0].Sigil is null);
        }
        finally { File.Delete(path); }
    }

    private static void Remove(JsonNode root, string path)
    {
        var parts = path.Split('/'); var node = root;
        foreach (var part in parts[..^1]) node = node is JsonArray array ? array[int.Parse(part)]! : node[part]!;
        Assert.True(((JsonObject)node).Remove(parts[^1]), "The corruption test must remove an existing member: " + path);
    }

    public static void WinningVariantsRequireTheirChoiceState()
    {
        var accepted = new List<string>();
        void Reject(string label, CampaignMatchSnapshot snapshot)
        { if (snapshot.IsValid()) accepted.Add(label); }
        foreach (var variant in new[] { GumballId.Duelist, GumballId.Telepath })
        {
            var resources = CampaignSnapshot.Create(42) with { Inventory = new([], [], [variant]) };
            var game = new GameEngine(["You", "West", "Partner", "East"], 42, new MatchOptions(10000) { HandLimit = 3 }, new(resources));
            game.StartGame(3);
            game.ExecuteCampaignAction(0, new CampaignCommand.Bid(variant == GumballId.Duelist ? BidLevel.Four : BidLevel.Five, variant));
            Assert.True(game.ExportCampaignMatchSnapshot().IsValid(), "An unfinished auction needs no winner choice yet.");
            while (game.Phase == GamePhase.Bidding) game.ExecuteCampaignAction(game.CampaignActor!.Value, new CampaignCommand.Pass());
            var pending = game.ExportCampaignMatchSnapshot(); Assert.True(pending.IsValid());
            Reject(variant + " missing pending choice", pending with { PendingChoice = null, PendingActor = null });
            var terminal = GameEngine.RestoreCampaignMatch(pending); terminal.DeveloperEndMatch(0);
            Assert.True(terminal.ExportCampaignMatchSnapshot().IsValid(), "Developer terminal may retain an unfulfilled choice description without its actor.");
            if (variant == GumballId.Duelist) game.ExecuteCampaignAction(0, new CampaignCommand.SelectOpponent(1));
            else game.ExecuteCampaignAction(2, new CampaignCommand.RevealPartnerCard(game.CreateCampaignView(2).Players[2].VisibleCards[0].Id));
            var chosen = game.ExportCampaignMatchSnapshot(); Assert.True(chosen.IsValid());
            Reject(variant + " missing resolved selection", variant == GumballId.Duelist ? chosen with { DuelOpponent = null } : chosen with { TelepathDone = false });
            if (variant == GumballId.Telepath) Reject("Telepath missing reveal evidence", chosen with { Reveals = [] });
            game.ExecuteCampaignAction(0, new CampaignCommand.ChooseContract(ContractMode.Trump, Suit.Hearts));
            var playing = game.ExportCampaignMatchSnapshot(); Assert.True(playing.IsValid());
            Reject(variant + " playing without selection", variant == GumballId.Duelist ? playing with { DuelOpponent = null } : playing with { TelepathDone = false });
            if (variant == GumballId.Duelist) Reject("Duelist playing without use marker", playing with { DuelistUsedThisMatch = false });
            else Reject("Telepath playing without revealed card", playing with { Reveals = [] });
            var restored = GameEngine.RestoreCampaignMatch(playing);
            Assert.Equal(variant == GumballId.Duelist ? 2 : 4, restored.CreateCampaignView(0).Players.Count(p => !p.IsSittingOut));
        }
        Assert.Equal("", string.Join(", ", accepted), "A winning variant must retain its required pending or completed choice. Accepted: " + string.Join(", ", accepted));
    }
}
