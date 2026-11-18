using BidEuchre.Core;

namespace BidEuchre.App;

/// <summary>Authoritative paid Arcade outcomes. Clients animate these saved results; they never submit scores.</summary>
public static class CampaignArcade
{
    public const int Price = 3, PlinkoLaneCount = 8, PlinkoSteps = 14, RoulettePocketCount = 12, PackSize = 3, LegendaryOdds = 50, JokerSlots = 5;
    public const string LegendaryKey = "Legendary";
    public static bool IsPack(ArcadeResult result) => result.PrizeCategory == FeatureCategory.Sigil && result.PackCards is not null;
    public static IReadOnlyList<int> PlinkoScores { get; } = Array.AsReadOnly(new[] { 6, 5, 4, 3, 2, 1, 2, 3, 4, 5, 6 });
    public static IReadOnlyList<int> PlinkoRowMinimums { get; } = Array.AsReadOnly(new[] { 6, 5, 4, 3, 4, 3, 2, 3, 2, 1, 2, 1, 0, 1 });

    public static int PlinkoScore(int landing)
        => landing >= 0 && landing < PlinkoScores.Count ? PlinkoScores[landing] : throw new GameRuleException("Choose a valid Plinko bin.");
    public static int PlinkoStartPosition(int lane) => lane is >= 0 and < PlinkoLaneCount
        ? 6 + lane * 2 : throw new GameRuleException("Choose a Plinko lane from 0 to 7.");
    public static int PlinkoLanding(int position) => position is >= 0 and <= 26
        ? Math.Clamp((78 + 40 * position - 64) / 98, 0, PlinkoScores.Count - 1) : throw new GameRuleException("Invalid Plinko position.");
    /// <summary>Advance to peg row 1–13 or the final bin row 14; reflect at the artwork's row edges.</summary>
    public static int PlinkoNextPosition(int position, int step, int direction)
    {
        if (step is < 1 or > PlinkoSteps || position is < 0 or > 26 || direction is not (-1 or 1))
            throw new GameRuleException("Invalid Plinko path.");
        var minimum = step == PlinkoSteps ? 0 : PlinkoRowMinimums[step];
        var next = position + direction;
        return next < minimum || next > 26 - minimum ? position - direction : next;
    }
    private static FeatureCategory? PrizeCategory(ArcadeGame game, int score) => game == ArcadeGame.Plinko
        ? score >= 5 ? FeatureCategory.Sigil : score >= 3 ? FeatureCategory.Tarot : null
        : score == 100 ? FeatureCategory.Sigil : score == 50 ? FeatureCategory.Tarot : null;
    public static int RouletteScore(int choice, int landing)
    {
        if (choice is < 1 or > RoulettePocketCount || landing is < 1 or > RoulettePocketCount)
            throw new GameRuleException("Choose a roulette pocket from 1 to 12.");
        return choice == landing ? 100 : choice % 2 == landing % 2 ? 50 : 0;
    }
    internal static string PlayId(int visit, ArcadeGame game) => $"arcade:{visit}:{game.ToString().ToLowerInvariant()}";
    internal static void RequireNoPendingResult(CampaignRun run)
    {
        if (run.Shop?.Arcade?.Pending is not null) throw new GameRuleException("Collect your Arcade prize or finish its result first.");
    }

    private static ArcadeState RequirePlay(CampaignRun run, ArcadeGame game, long? expectedRevision)
    {
        CampaignShop.RequireCurrentShop(run, expectedRevision);
        CampaignShop.RequireNoPrintedTickets(run);
        if (run.IsOver || run.Shop!.MatchJustFinished != run.Match - 1) throw new GameRuleException("The Arcade is closed.");
        var arcade = run.Shop.Arcade ?? throw new GameRuleException("Open the shop before playing the Arcade.");
        if (game == ArcadeGame.Plinko ? arcade.PlinkoPlayed : arcade.RoulettePlayed)
            throw new GameRuleException("You have already played this game during this shop visit.");
        if (run.Money < Price) throw new GameRuleException("Not enough money.");
        return arcade;
    }

    public static CampaignRun PlayPlinko(CampaignRun run, int lane, long? expectedRevision = null)
    {
        if (lane is < 0 or >= PlinkoLaneCount) throw new GameRuleException("Choose a Plinko lane from 0 to 7.");
        var arcade = RequirePlay(run, ArcadeGame.Plinko, expectedRevision);
        var random = new CampaignRandom(run.Resources.RandomState);
        var path = new int[PlinkoSteps + 1]; path[0] = PlinkoStartPosition(lane);
        for (var step = 1; step < path.Length; step++)
        {
            path[step] = PlinkoNextPosition(path[step - 1], step, random.NextInt(2) == 0 ? -1 : 1);
        }
        var landing = PlinkoLanding(path[^1]);
        return CompletePlay(run, arcade with { PlinkoPlayed = true }, ArcadeGame.Plinko, lane, landing, PlinkoScore(landing), path, random);
    }

    public static CampaignRun PlayRoulette(CampaignRun run, int pocket, long? expectedRevision = null)
    {
        if (pocket is < 1 or > RoulettePocketCount) throw new GameRuleException("Choose a roulette pocket from 1 to 12.");
        var arcade = RequirePlay(run, ArcadeGame.Roulette, expectedRevision);
        var random = new CampaignRandom(run.Resources.RandomState);
        var landing = random.NextInt(RoulettePocketCount) + 1;
        return CompletePlay(run, arcade with { RoulettePlayed = true }, ArcadeGame.Roulette, pocket, landing,
            RouletteScore(pocket, landing), [], random);
    }

    private static CampaignRun CompletePlay(CampaignRun run, ArcadeState arcade, ArcadeGame game, int choice, int landing,
        int score, int[] path, CampaignRandom random)
    {
        var category = PrizeCategory(game, score);
        string? feature = null;
        string[]? pack = null;
        CardId[] targets = [];
        if (category is { } prizeCategory)
        {
            if (prizeCategory == FeatureCategory.Sigil) pack = DrawPack(run, random);
            else
            {
                var pool = FeatureCatalog.All.Where(f => f.Category == prizeCategory).ToArray();
                feature = pool[random.NextInt(pool.Length)].Key;
            }
            targets = CampaignShop.DrawTargets(run, random);
            if (targets.Length != CampaignShop.ShopTargetCount) throw new GameRuleException("The Arcade needs six eligible deck cards for a prize.");
        }
        var result = new ArcadeResult(PlayId(run.Shop!.MatchJustFinished, game), game, choice, landing, score, path, category, feature, targets)
        { PackCards = pack };
        var resources = run.Resources.Copy();
        resources.Wallets[0] = run.Money - Price;
        return run with
        {
            Money = resources.Wallets[0],
            Resources = resources with { RandomState = random.State, Revision = resources.Revision + 1 },
            Shop = run.Shop with { Arcade = arcade with { Pending = result } }
        };
    }

    /// <summary>Three distinct cards; each slot may be the Legendary (at most one) while a Joker slot is free.</summary>
    private static string[] DrawPack(CampaignRun run, CampaignRandom random)
    {
        var sigils = Enum.GetNames<SigilId>().ToList();
        var canLegendary = run.Resources.Inventory.Jokers.Length < JokerSlots;
        var pack = new string[PackSize];
        for (var slot = 0; slot < PackSize; slot++)
        {
            if (canLegendary && !pack.Contains(LegendaryKey) && random.NextInt(LegendaryOdds) == 0) { pack[slot] = LegendaryKey; continue; }
            var index = random.NextInt(sigils.Count);
            pack[slot] = sigils[index]; sigils.RemoveAt(index);
        }
        return pack;
    }

    private static void RequireNotPack(ArcadeResult result)
    {
        if (IsPack(result)) throw new GameRuleException("Open your Collector's Pack and choose a card.");
    }

    private static ArcadeResult RequireResult(CampaignRun run, string playId, long? expectedRevision)
    {
        CampaignShop.RequireCurrentShop(run, expectedRevision, allowArcadeResult: true);
        if (run.IsOver || run.Shop!.MatchJustFinished != run.Match - 1 || run.Shop.Arcade?.Pending is not { } pending
            || pending.PlayId != playId) throw new GameRuleException("Choose the current Arcade result.");
        return pending;
    }

    public static CampaignRun KeepArcadePrize(CampaignRun run, string playId, long? expectedRevision = null)
    {
        var prize = RequireResult(run, playId, expectedRevision);
        if (prize.PrizeCategory is null) throw new GameRuleException("This play did not win a prize.");
        RequireNotPack(prize);
        if (!run.Resources.Inventory.HasItemRoom) throw new GameRuleException("Your card slots are full. Discard a stored card first, or use the prize now.");
        var resources = run.Resources.Copy();
        var inventory = resources.Inventory;
        inventory = prize.PrizeCategory == FeatureCategory.Sigil
            ? inventory with { Sigils = [.. inventory.Sigils, Enum.Parse<SigilId>(prize.FeatureId!)] }
            : inventory with { Tarot = [.. inventory.Tarot, Enum.Parse<TarotId>(prize.FeatureId!)] };
        resources.Wallets[0] = run.Money;
        return run with
        {
            Resources = resources with { Inventory = inventory, Revision = resources.Revision + 1 },
            Shop = run.Shop! with { Arcade = run.Shop.Arcade! with { Pending = null } }
        };
    }

    public static CampaignRun BeginArcadePrizeUse(CampaignRun run, string playId, long? expectedRevision = null)
    {
        var prize = RequireResult(run, playId, expectedRevision);
        if (prize.PrizeCategory is not { } category) throw new GameRuleException("This play did not win a prize.");
        RequireNotPack(prize);
        return StartPrizeUse(run, prize, category, prize.FeatureId!);
    }

    /// <summary>Open a Collector's Pack: a Sigil must be applied to one of the six cards; the Legendary fills free Joker slots.</summary>
    public static CampaignRun PickPackCard(CampaignRun run, string playId, int index, long? expectedRevision = null)
    {
        var prize = RequireResult(run, playId, expectedRevision);
        if (!IsPack(prize)) throw new GameRuleException("This Arcade result is not a Collector's Pack.");
        if (index < 0 || index >= prize.PackCards!.Length) throw new GameRuleException("Choose one of the three cards.");
        var card = prize.PackCards[index];
        if (card != LegendaryKey) return StartPrizeUse(run, prize, FeatureCategory.Sigil, card);
        if (run.Resources.Inventory.Jokers.Length >= JokerSlots) throw new GameRuleException("Your Joker slots are full. Choose a Sigil card instead.");
        var resources = run.Resources.Copy();
        var random = new CampaignRandom(resources.RandomState);
        var inventory = resources.Inventory;
        var pool = FeatureCatalog.All.Where(f => f.Category == FeatureCategory.Joker)
            .Select(f => Enum.Parse<JokerId>(f.Key)).Where(id => !inventory.Has(id)).ToList();
        var granted = new List<JokerState>();
        for (var free = JokerSlots - inventory.Jokers.Length; free > 0 && pool.Count > 0; free--)
        {
            var pick = random.NextInt(pool.Count);
            granted.Add(new JokerState(pool[pick])); pool.RemoveAt(pick);
        }
        resources.Wallets[0] = run.Money;
        return run with
        {
            Resources = resources with
            {
                Inventory = inventory with { Jokers = [.. inventory.Jokers, .. granted] },
                RandomState = random.State, Revision = resources.Revision + 1
            },
            Shop = run.Shop! with { Arcade = run.Shop.Arcade! with { Pending = null } }
        };
    }

    private static CampaignRun StartPrizeUse(CampaignRun run, ArcadeResult prize, FeatureCategory category, string featureId)
    {
        var id = prize.PlayId + ":prize";
        if (run.Shop!.Offers.Any(o => o.Id == id)) throw new GameRuleException("This Arcade prize has already been collected.");
        var offer = new ShopOffer(id, category, featureId, 0, true) { IsArcadePrize = true, TargetCards = prize.TargetCards.ToArray() };
        var resources = run.Resources.Copy(); resources.Wallets[0] = run.Money;
        return run with
        {
            Resources = resources with { Revision = resources.Revision + 1 },
            Shop = run.Shop with
            {
                Offers = [.. run.Shop.Offers, offer], Arcade = run.Shop.Arcade! with { Pending = null },
                UseSession = new(id, category, featureId, false)
            }
        };
    }

    public static CampaignRun DismissArcadeLoss(CampaignRun run, string playId, long? expectedRevision = null)
    {
        var result = RequireResult(run, playId, expectedRevision);
        if (result.PrizeCategory is not null) throw new GameRuleException("Keep or use your Arcade prize first.");
        return run with
        {
            Resources = run.Resources with { Revision = run.Resources.Revision + 1 },
            Shop = run.Shop! with { Arcade = run.Shop.Arcade! with { Pending = null } }
        };
    }

    private static bool ValidPack(string[] pack) => pack.Length == PackSize && pack.Distinct().Count() == PackSize
        && pack.All(k => k == LegendaryKey || Enum.GetNames<SigilId>().Contains(k));

    internal static bool IsValid(ShopState shop, CardState[] deck)
    {
        if (shop.Arcade is not { } arcade) return shop.Offers.All(o => !o.IsArcadePrize);
        foreach (var offer in shop.Offers.Where(o => o.IsArcadePrize))
        {
            if (!offer.Sold || offer.Price != 0 || offer.Printed is not null || offer.TargetCards is null
                || offer.Category is not (FeatureCategory.Sigil or FeatureCategory.Tarot)) return false;
            if (!(arcade.PlinkoPlayed && offer.Id == PlayId(shop.MatchJustFinished, ArcadeGame.Plinko) + ":prize")
                && !(arcade.RoulettePlayed && offer.Id == PlayId(shop.MatchJustFinished, ArcadeGame.Roulette) + ":prize")) return false;
        }
        if (arcade.Pending is not { } pending) return true;
        if (!Enum.IsDefined(pending.Game) || pending.PlayId != PlayId(shop.MatchJustFinished, pending.Game)
            || pending.Path is null || pending.TargetCards is null || shop.UseSession is not null || shop.Offers.Any(o => o.Printed is not null)
            || shop.Offers.Any(o => o.Id == pending.PlayId + ":prize")) return false;
        if (pending.Game == ArcadeGame.Plinko)
        {
            if (!arcade.PlinkoPlayed || pending.Choice is < 0 or >= PlinkoLaneCount || pending.Landing < 0 || pending.Landing >= PlinkoScores.Count
                || pending.Path.Length != PlinkoSteps + 1 || pending.Path[0] != PlinkoStartPosition(pending.Choice)
                || pending.Path.Any(p => p is < 0 or > 26) || pending.Path[^1] % 2 != 0 || PlinkoLanding(pending.Path[^1]) != pending.Landing
                || pending.Path.Take(PlinkoSteps).Where((p, row) => p < PlinkoRowMinimums[row] || p > 26 - PlinkoRowMinimums[row]).Any()
                || pending.Path.Skip(1).Where((p, i) => Math.Abs(p - pending.Path[i]) != 1).Any()
                || pending.Score != PlinkoScore(pending.Landing)) return false;
        }
        else if (!arcade.RoulettePlayed || pending.Choice is < 1 or > RoulettePocketCount || pending.Landing is < 1 or > RoulettePocketCount
            || pending.Path.Length != 0 || pending.Score != RouletteScore(pending.Choice, pending.Landing)) return false;
        var expected = PrizeCategory(pending.Game, pending.Score);
        if (pending.PrizeCategory != expected) return false;
        if (expected is null) return pending.FeatureId is null && pending.PackCards is null && pending.TargetCards.Length == 0;
        var prizeShape = pending.PackCards is { } pack
            ? expected == FeatureCategory.Sigil && pending.FeatureId is null && ValidPack(pack)
            : FeatureCatalog.All.Any(f => f.Category == expected && f.Key == pending.FeatureId);
        return prizeShape
            && pending.TargetCards.Length == CampaignShop.ShopTargetCount && pending.TargetCards.Distinct().Count() == pending.TargetCards.Length
            && pending.TargetCards.All(id => deck.Any(card => card.Id == id));
    }
}
