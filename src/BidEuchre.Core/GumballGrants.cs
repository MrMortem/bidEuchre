namespace BidEuchre.Core;

public sealed record GumballGrant(CampaignInventory Inventory, ulong RandomState, GumballId Id);

/// <summary>The shared deterministic acquisition rule for paid pulls and Sweet Tooth uses.</summary>
public static class GumballGrants
{
    public static GumballGrant Pull(CampaignInventory inventory, ulong randomState)
    {
        var all = Enum.GetValues<GumballId>();
        var locked = all.Except(inventory.Unlocks).ToArray();
        var choices = locked.Length > 0 ? locked : all;
        // Keep RNG and inventory private until the complete grant succeeds.
        var random = new CampaignRandom(randomState);
        var id = choices[random.NextInt(choices.Length)];
        var updated = inventory.Copy();
        if (locked.Length > 0) updated = updated with { Unlocks = [.. updated.Unlocks, id] };
        else
        {
            if (updated.GumballExtraCopies.Values.Sum(count => (long)count) >= int.MaxValue)
                throw new GameRuleException("The gumball copy count cannot increase further.");
            updated.GumballExtraCopies[id] = checked(updated.GumballExtraCopies.GetValueOrDefault(id) + 1);
        }
        return new(updated, random.State, id);
    }
}

/// <summary>A fixed duplicate gumball reward. Team uses the engine's zero-based team index.</summary>
public sealed record GumballBidBonus(int Team, GumballId Id, int Points);

public static class GumballRewards
{
    /// <summary>Per-type contributions for result explanations. These points apply once after all hand multipliers.</summary>
    public static GumballBidBonus[] SuccessfulBidBonuses(CampaignInventory inventory, int bidder, bool success,
        BidLevel bid, ContractMode mode, GumballId? variant)
    {
        if (bidder != 0 || !success) return [];
        var matching = new HashSet<GumballId>();
        if (variant is GumballId.Chaos or GumballId.Poker or GumballId.Duelist or GumballId.Telepath) matching.Add(variant.Value);
        if ((int)bid == 2) matching.Add(GumballId.Lowballer);
        if (bid == BidLevel.PartnersBest) matching.Add(GumballId.PartnersBest);
        if (bid == BidLevel.Alone) matching.Add(GumballId.Alone);
        if (mode is ContractMode.High or ContractMode.Low) matching.Add(GumballId.HighLow);
        return matching.OrderBy(id => id).Select(id => new GumballBidBonus(0, id, inventory.GumballExtraCopies.GetValueOrDefault(id)))
            .Where(bonus => bonus.Points > 0).ToArray();
    }
}
