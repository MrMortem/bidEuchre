using BidEuchre.Core;

namespace BidEuchre.App;

public enum ConsoleTone { Normal, Muted, Ok, Error, Accent, Banner }
public sealed record ConsoleLine(string Text, ConsoleTone Tone = ConsoleTone.Normal);

/// <summary>What the developer console can reach. The desktop app routes each change to the live match or to the saved run.</summary>
public interface IDeveloperHost
{
    /// <summary>The current campaign run, kept in step with a live match; null when no run is loaded.</summary>
    CampaignRun? Run { get; }
    bool InMatch { get; }
    /// <summary>Team scores of the live campaign match, or null outside one.</summary>
    IReadOnlyList<int>? Scores { get; }
    /// <summary>The human's hand in the live match, or null outside one.</summary>
    IReadOnlyList<CardId>? HumanHand { get; }
    /// <summary>Changes deck, inventory and wallets, in the live match or the saved run.</summary>
    Task UpdateResourcesAsync(Func<CampaignSnapshot, CampaignSnapshot> update);
    /// <summary>Changes the run itself (lives, match, shop). Only between matches.</summary>
    Task UpdateRunAsync(Func<CampaignRun, CampaignRun> update);
    Task SetScoresAsync(int us, int them);
    Task EndMatchAsync(bool won);
}

/// <summary>Parses and runs developer-console commands against an <see cref="IDeveloperHost"/>.</summary>
public sealed class DeveloperConsole(IDeveloperHost host)
{
    public static readonly string[] Banner =
    [
        @"___________     ____.  _________",
        @"\__    ___/    |    | /   _____/",
        @"  |    |       |    | \_____  \ ",
        @"  |    |   /\__|    | /        \",
        @"  |____|   \________|/_______  /",
        @"                             \/ ",
    ];

    private sealed record Command(string Name, string Usage, string Summary, Func<string[], Task<IEnumerable<ConsoleLine>>> Run, params string[] Aliases);
    private readonly List<string> _history = [];
    private Command[]? _commands;

    public IReadOnlyList<string> History => _history;

    public static IEnumerable<ConsoleLine> Greeting() =>
        Banner.Select(line => new ConsoleLine(line, ConsoleTone.Banner))
            .Append(new("  developer console · type 'help' for commands · ` to close", ConsoleTone.Muted));

    public IEnumerable<string> CommandNames => Commands.SelectMany(c => c.Aliases.Prepend(c.Name)).Order();

    public async Task<IReadOnlyList<ConsoleLine>> ExecuteAsync(string input)
    {
        var line = input.Trim();
        if (line.Length == 0) return [];
        _history.Add(line);
        var output = new List<ConsoleLine> { new($"> {line}", ConsoleTone.Accent) };
        // Several commands can be chained with ';'.
        foreach (var part in line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var args = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = Find(args[0]);
            if (command is null) { output.Add(Error($"Unknown command '{args[0]}'. Type 'help'.")); continue; }
            try { output.AddRange(await command.Run(args[1..])); }
            catch (Exception error) when (error is DeveloperCommandException or GameRuleException or InvalidOperationException)
            { output.Add(Error(error.Message)); }
        }
        return output;
    }

    private Command? Find(string name) => Commands.FirstOrDefault(c =>
        c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || c.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)));

    private Command[] Commands => _commands ??=
    [
        new("help", "help [command]", "List commands, or explain one.", Help, "?"),
        new("status", "status", "Show the run: lives, money, ante, match, scores.", _ => Task.FromResult(Status()), "st"),
        new("inv", "inv", "Show held Jokers, Tarot, Sigils and unlocked bids.", _ => Task.FromResult(Inventory()), "inventory"),
        new("money", "money <n> | money +n | money -n", "Set, add or remove money.", Money, "cash", "$"),
        new("lives", "lives <n> | lives +n | lives -n", "Set, add or remove lives (between matches).", Lives, "hp"),
        new("joker", "joker add|remove <name> | joker value <name> <n> | joker clear | joker list", "Manage Jokers (5 slots).", Joker, "j"),
        new("tarot", "tarot add <name> [count] | tarot remove <name|slot#> | tarot clear | tarot list", "Manage Tarot cards (share 4 item slots with Sigils).", Tarot, "t"),
        new("sigil", "sigil add <name> [count] | sigil remove <name|slot#> | sigil clear | sigil list", "Manage held Sigil cards.", Sigil),
        new("gumball", "gumball unlock|lock <name|all> | gumball list", "Unlock or lock bidding options.", Gumball, "bid"),
        new("card", "card <ref> enhance|sigil <name|none> | card <ref> suit <suit> | card <ref> rank <rank>", "Edit deck cards. <ref> is an id (0-23), a face like AS or 10h, 'hand', or 'all'.", Card),
        new("deck", "deck [list] | deck reset", "Show the shared deck or restore the starting deck.", Deck),
        new("match", "match <n>", "Jump to campaign match n (1-15) between matches.", Match),
        new("ante", "ante <n>", "Jump to the first match of ante n between matches.", Ante),
        new("shop", "shop reroll | shop clear", "Restock the shop with new offers, or empty it.", Shop),
        new("score", "score <us> <them>", "Set the live match score.", Score),
        new("win", "win", "Win the live match now.", async _ => { await host.EndMatchAsync(true); return [Ok("Match won.")]; }),
        new("lose", "lose", "Lose the live match now.", async _ => { await host.EndMatchAsync(false); return [Ok("Match lost.")]; }),
        new("god", "god", "Max money, 5 lives, every bid unlocked.", God),
        new("list", "list jokers|tarot|sigils|gumballs|enhancements", "List every catalog entry of a kind.", List, "ls"),
        new("banner", "banner", "Show the greeting again.", _ => Task.FromResult(Greeting())),
        new("history", "history", "Show the commands entered so far.", _ => Task.FromResult(_history.Select((h, i) => new ConsoleLine($"{i + 1,3}  {h}", ConsoleTone.Muted)))),
    ];

    // ---- informational ----

    private Task<IEnumerable<ConsoleLine>> Help(string[] args)
    {
        if (args.Length > 0)
        {
            var command = Find(args[0]) ?? throw new DeveloperCommandException($"Unknown command '{args[0]}'.");
            return Task.FromResult<IEnumerable<ConsoleLine>>(
            [
                new(command.Usage, ConsoleTone.Accent), new(command.Summary),
                .. command.Aliases.Length > 0 ? new[] { new ConsoleLine($"aliases: {string.Join(", ", command.Aliases)}", ConsoleTone.Muted) } : []
            ]);
        }
        var width = Commands.Max(c => c.Name.Length) + 2;
        return Task.FromResult(Commands.Select(c => new ConsoleLine($"{c.Name.PadRight(width)}{c.Summary}"))
            .Append(new("clear     Clear the console.  Chain commands with ';'.  ↑/↓ history, Tab completes.", ConsoleTone.Muted)));
    }

    private IEnumerable<ConsoleLine> Status()
    {
        var run = RequireRun();
        var lines = new List<ConsoleLine>
        {
            new($"lives {run.Lives}   money ${Wallet(run)}   ante {run.Ante}   match {run.Match} ({run.MatchInAnte}/3{(run.IsBoss ? " boss" : "")})   target {run.Threshold}"),
            new($"phase {(host.InMatch ? "in match" : run.Phase.ToString())}   won {run.MatchesWon}   revision {run.Resources.Revision}", ConsoleTone.Muted)
        };
        if (host.Scores is { } scores) lines.Add(new($"score us {scores[0]} – them {scores[1]}"));
        return lines;
    }

    private IEnumerable<ConsoleLine> Inventory()
    {
        var inventory = RequireRun().Resources.Inventory;
        string Joined(IEnumerable<string> names) => names.Any() ? string.Join(", ", names) : "—";
        return
        [
            new($"jokers  [{inventory.Jokers.Length}/5]  {Joined(inventory.Jokers.Select(j => Name(FeatureCategory.Joker, j.Id.ToString()) + (j.Value != 0 ? $" ({j.Value:+0;-0})" : "")))}"),
            new($"tarot   [{inventory.ItemCount}/{CampaignInventory.ItemSlots}]  {Joined(inventory.Tarot.Select((t, i) => $"#{i + 1} {Name(FeatureCategory.Tarot, t.ToString())}"))}"),
            new($"sigils          {Joined(inventory.Sigils.Select((s, i) => $"#{inventory.Tarot.Length + i + 1} {Name(FeatureCategory.Sigil, s.ToString())}"))}"),
            new($"bids    [{inventory.Unlocks.Length}/{CampaignInventory.GumballCount}]  {Joined(inventory.Unlocks.Select(g => Name(FeatureCategory.Gumball, g.ToString())))}"),
        ];
    }

    private Task<IEnumerable<ConsoleLine>> List(string[] args)
    {
        var category = args.Length == 0 ? throw Usage("list jokers|tarot|sigils|gumballs|enhancements") : args[0].ToLowerInvariant() switch
        {
            "joker" or "jokers" or "j" => FeatureCategory.Joker,
            "tarot" or "tarots" or "t" => FeatureCategory.Tarot,
            "sigil" or "sigils" => FeatureCategory.Sigil,
            "gumball" or "gumballs" or "bids" or "bid" => FeatureCategory.Gumball,
            "enhancement" or "enhancements" or "enh" => FeatureCategory.Enhancement,
            _ => throw new DeveloperCommandException($"Unknown kind '{args[0]}'.")
        };
        return Task.FromResult(Catalog(category));
    }

    private static IEnumerable<ConsoleLine> Catalog(FeatureCategory category)
    {
        var entries = FeatureCatalog.All.Where(f => f.Category == category).ToArray();
        var width = entries.Max(f => f.Key.Length) + 2;
        return entries.Select(f => new ConsoleLine($"{f.Key.PadRight(width)}{f.Description}", ConsoleTone.Muted));
    }

    // ---- money and lives ----

    private async Task<IEnumerable<ConsoleLine>> Money(string[] args)
    {
        RequireRun();
        if (args.Length != 1) throw Usage("money <n> | money +n | money -n");
        var amount = 0;
        await host.UpdateResourcesAsync(s =>
        {
            amount = Math.Max(0, Adjust(s.Wallets[0], args[0]));
            var wallets = s.Wallets.ToArray(); wallets[0] = amount;
            return s with { Wallets = wallets };
        });
        return [Ok($"Money is now ${amount}.")];
    }

    private async Task<IEnumerable<ConsoleLine>> Lives(string[] args)
    {
        var run = RequireRun(); RequireBetweenMatches();
        if (args.Length != 1) throw Usage("lives <n> | lives +n | lives -n");
        var lives = Adjust(run.Lives, args[0]);
        if (lives < 1) throw new DeveloperCommandException("A run needs at least 1 life. Use 'lose' to end one.");
        await host.UpdateRunAsync(r => r with { Lives = lives });
        return [Ok($"Lives are now {lives}.")];
    }

    // ---- inventory ----

    private async Task<IEnumerable<ConsoleLine>> Joker(string[] args)
    {
        RequireRun();
        var verb = args.FirstOrDefault()?.ToLowerInvariant();
        switch (verb)
        {
            case "list": return Catalog(FeatureCategory.Joker);
            case "clear":
                await Inventory(i => i with { Jokers = [] });
                return [Ok("All Jokers removed.")];
            case "add" when args.Length >= 2:
            {
                var id = Resolve<JokerId>(FeatureCategory.Joker, Rest(args, 1));
                await Inventory(i =>
                {
                    if (i.Has(id)) throw new DeveloperCommandException($"{Name(FeatureCategory.Joker, id.ToString())} is already held.");
                    if (i.Jokers.Length >= 5) throw new DeveloperCommandException("All 5 Joker slots are full. Remove one first.");
                    return i with { Jokers = [.. i.Jokers, new(id)] };
                });
                return [Ok($"Added {Name(FeatureCategory.Joker, id.ToString())}.")];
            }
            case "remove" or "rm" or "del" when args.Length >= 2:
            {
                var id = Resolve<JokerId>(FeatureCategory.Joker, Rest(args, 1));
                await Inventory(i => i.Has(id) ? i with { Jokers = i.Jokers.Where(j => j.Id != id).ToArray() }
                    : throw new DeveloperCommandException($"{Name(FeatureCategory.Joker, id.ToString())} is not held."));
                return [Ok($"Removed {Name(FeatureCategory.Joker, id.ToString())}.")];
            }
            case "value" when args.Length >= 3 && int.TryParse(args[^1], out var value):
            {
                var id = Resolve<JokerId>(FeatureCategory.Joker, string.Join(' ', args[1..^1]));
                await Inventory(i => i.Has(id) ? i with { Jokers = i.Jokers.Select(j => j.Id == id ? j with { Value = value } : j).ToArray() }
                    : throw new DeveloperCommandException($"{Name(FeatureCategory.Joker, id.ToString())} is not held."));
                return [Ok($"{Name(FeatureCategory.Joker, id.ToString())} value set to {value}.")];
            }
            default: throw Usage("joker add|remove <name> | joker value <name> <n> | joker clear | joker list");
        }
    }

    private Task<IEnumerable<ConsoleLine>> Tarot(string[] args) => Items(args, FeatureCategory.Tarot);
    private Task<IEnumerable<ConsoleLine>> Sigil(string[] args) => Items(args, FeatureCategory.Sigil);

    private async Task<IEnumerable<ConsoleLine>> Items(string[] args, FeatureCategory category)
    {
        RequireRun();
        var noun = category == FeatureCategory.Tarot ? "tarot" : "sigil";
        var verb = args.FirstOrDefault()?.ToLowerInvariant();
        switch (verb)
        {
            case "list": return Catalog(category);
            case "clear":
                await Inventory(i => category == FeatureCategory.Tarot ? i with { Tarot = [] } : i with { Sigils = [] });
                return [Ok($"All {noun} cards removed.")];
            case "add" when args.Length >= 2:
            {
                var count = 1;
                var nameArgs = args[1..];
                if (nameArgs.Length > 1 && int.TryParse(nameArgs[^1], out var parsed)) { count = parsed; nameArgs = nameArgs[..^1]; }
                if (count < 1) throw new DeveloperCommandException("Count must be at least 1.");
                var key = ResolveKey(category, string.Join(' ', nameArgs));
                await Inventory(i =>
                {
                    if (i.ItemCount + count > CampaignInventory.ItemSlots)
                        throw new DeveloperCommandException($"Only {CampaignInventory.ItemSlots - i.ItemCount} of {CampaignInventory.ItemSlots} item slots are free.");
                    return category == FeatureCategory.Tarot
                        ? i with { Tarot = [.. i.Tarot, .. Enumerable.Repeat(Enum.Parse<TarotId>(key), count)] }
                        : i with { Sigils = [.. i.Sigils, .. Enumerable.Repeat(Enum.Parse<SigilId>(key), count)] };
                });
                return [Ok($"Added {count}× {Name(category, key)}.")];
            }
            case "remove" or "rm" or "del" when args.Length >= 2:
            {
                var removed = "";
                await Inventory(i =>
                {
                    var held = category == FeatureCategory.Tarot ? i.Tarot.Select(t => t.ToString()).ToList() : i.Sigils.Select(s => s.ToString()).ToList();
                    int index;
                    if (int.TryParse(args[1].TrimStart('#'), out var slot)) index = slot - 1 - (category == FeatureCategory.Sigil ? i.Tarot.Length : 0);
                    else index = held.IndexOf(ResolveKey(category, Rest(args, 1)));
                    if (index < 0 || index >= held.Count) throw new DeveloperCommandException($"No such {noun} card is held.");
                    removed = Name(category, held[index]);
                    held.RemoveAt(index);
                    return category == FeatureCategory.Tarot ? i with { Tarot = held.Select(Enum.Parse<TarotId>).ToArray() }
                        : i with { Sigils = held.Select(Enum.Parse<SigilId>).ToArray() };
                });
                return [Ok($"Removed {removed}.")];
            }
            default: throw Usage($"{noun} add <name> [count] | {noun} remove <name|slot#> | {noun} clear | {noun} list");
        }
    }

    private async Task<IEnumerable<ConsoleLine>> Gumball(string[] args)
    {
        RequireRun();
        var verb = args.FirstOrDefault()?.ToLowerInvariant();
        if (verb == "list") return Catalog(FeatureCategory.Gumball);
        if (verb is not ("unlock" or "lock") || args.Length < 2) throw Usage("gumball unlock|lock <name|all> | gumball list");
        var all = args[1].Equals("all", StringComparison.OrdinalIgnoreCase);
        var ids = all ? Enum.GetValues<GumballId>() : [Resolve<GumballId>(FeatureCategory.Gumball, Rest(args, 1))];
        await Inventory(i => verb == "unlock"
            ? i with { Unlocks = i.Unlocks.Union(ids).ToArray() }
            : i with { Unlocks = i.Unlocks.Except(ids).ToArray() });
        var what = all ? "every bid" : Name(FeatureCategory.Gumball, ids[0].ToString());
        return [Ok($"{(verb == "unlock" ? "Unlocked" : "Locked")} {what}.")];
    }

    // ---- deck ----

    private async Task<IEnumerable<ConsoleLine>> Card(string[] args)
    {
        var run = RequireRun();
        if (args.Length < 3) throw Usage("card <ref> enhance|sigil <name|none> | card <ref> suit <suit> | card <ref> rank <rank>");
        var targets = CardRefs(run.Resources.Deck, args[0]);
        var value = Rest(args, 2);
        var none = value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("clear", StringComparison.OrdinalIgnoreCase);
        Func<CardState, CardState> change = args[1].ToLowerInvariant() switch
        {
            "enhance" or "enh" or "e" => none ? c => c with { Enhancement = null }
                : Resolve<EnhancementId>(FeatureCategory.Enhancement, value) is var enhancement ? c => c with { Enhancement = enhancement } : null!,
            "sigil" or "s" => none ? c => c with { Sigil = null }
                : Resolve<SigilId>(FeatureCategory.Sigil, value) is var sigil ? c => c with { Sigil = sigil } : null!,
            "suit" => ParseSuit(value) is var suit ? c => CampaignCards.ChangeSuit(c, suit) : null!,
            "rank" => ParseRank(value) is var rank ? c => c with { Face = c.Face with { Rank = rank } } : null!,
            _ => throw new DeveloperCommandException($"Unknown card change '{args[1]}'. Use enhance, sigil, suit or rank.")
        };
        await host.UpdateResourcesAsync(s => s with { Deck = s.Deck.Select(c => targets.Contains(c.Id) ? change(c) : c).ToArray() });
        var deck = RequireRun().Resources.Deck;
        return targets.Count == 1 ? [Ok($"Card #{targets.First().Value} is now {Describe(deck.Single(c => c.Id == targets.First()))}.")]
            : [Ok($"Changed {targets.Count} cards.")];
    }

    private async Task<IEnumerable<ConsoleLine>> Deck(string[] args)
    {
        var run = RequireRun();
        var verb = args.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        if (verb == "reset")
        {
            await host.UpdateResourcesAsync(s => s with { Deck = CampaignCards.CreateDeck().ToArray() });
            return [Ok("The deck is back to its starting 24 cards.")];
        }
        if (verb != "list") throw Usage("deck [list] | deck reset");
        var hand = host.HumanHand?.ToHashSet() ?? [];
        return run.Resources.Deck.OrderBy(c => c.Id.Value)
            .Select(c => new ConsoleLine($"#{c.Id.Value,-3}{Describe(c)}{(hand.Contains(c.Id) ? "   ← in hand" : "")}",
                c.Enhancement is null && c.Sigil is null ? ConsoleTone.Muted : ConsoleTone.Normal));
    }

    private HashSet<CardId> CardRefs(IReadOnlyList<CardState> deck, string reference)
    {
        if (reference.Equals("all", StringComparison.OrdinalIgnoreCase)) return deck.Select(c => c.Id).ToHashSet();
        if (reference.Equals("hand", StringComparison.OrdinalIgnoreCase))
            return host.HumanHand is { Count: > 0 } hand ? hand.ToHashSet() : throw new DeveloperCommandException("There is no hand outside a match.");
        if (int.TryParse(reference.TrimStart('#'), out var id))
            return id is >= 0 and < 24 ? [new CardId(id)] : throw new DeveloperCommandException("Card ids run from 0 to 23. Try 'deck'.");
        if (reference.Length >= 2)
        {
            var rank = ParseRank(reference[..^1]);
            var suit = ParseSuit(reference[^1..]);
            var matches = deck.Where(c => c.Face == new Card(suit, rank)).Select(c => c.Id).ToHashSet();
            return matches.Count > 0 ? matches : throw new DeveloperCommandException($"No {reference.ToUpperInvariant()} is in the deck.");
        }
        throw new DeveloperCommandException($"'{reference}' is not a card. Use an id, a face like AS, 'hand' or 'all'.");
    }

    private static Suit ParseSuit(string text) => text.ToLowerInvariant() switch
    {
        "c" or "club" or "clubs" or "♣" => Suit.Clubs,
        "d" or "diamond" or "diamonds" or "♦" => Suit.Diamonds,
        "h" or "heart" or "hearts" or "♥" => Suit.Hearts,
        "s" or "spade" or "spades" or "♠" => Suit.Spades,
        _ => throw new DeveloperCommandException($"'{text}' is not a suit.")
    };

    private static Rank ParseRank(string text) => text.ToLowerInvariant() switch
    {
        "9" or "nine" => Rank.Nine,
        "10" or "t" or "ten" => Rank.Ten,
        "j" or "jack" => Rank.Jack,
        "q" or "queen" => Rank.Queen,
        "k" or "king" => Rank.King,
        "a" or "ace" => Rank.Ace,
        _ => throw new DeveloperCommandException($"'{text}' is not a rank (9, 10, J, Q, K, A).")
    };

    private static string Describe(CardState card)
    {
        var symbol = card.Face.Suit switch { Suit.Clubs => "♣", Suit.Diamonds => "♦", Suit.Hearts => "♥", _ => "♠" };
        var extras = new[] { card.Enhancement?.ToString(), card.Sigil is { } s ? $"{s} sigil" : null }.Where(x => x is not null);
        var text = $"{Core.Card.DisplayRank(card.Face.Rank)}{symbol}";
        return extras.Any() ? $"{text,-4} {string.Join(" · ", extras)}" : text;
    }

    // ---- run flow ----

    private async Task<IEnumerable<ConsoleLine>> Match(string[] args)
    {
        RequireRun(); RequireBetweenMatches();
        if (args.Length != 1 || !int.TryParse(args[0], out var match) || match < 1 || match > CampaignRun.VictoryMatch)
            throw Usage($"match <1-{CampaignRun.VictoryMatch}>");
        await host.UpdateRunAsync(r => r with { Match = match, Shop = null });
        var run = RequireRun();
        return [Ok($"Now at ante {run.Ante}, match {run.MatchInAnte}/3{(run.IsBoss ? " (boss)" : "")}.")];
    }

    private Task<IEnumerable<ConsoleLine>> Ante(string[] args)
    {
        var last = (CampaignRun.VictoryMatch - 1) / CampaignRun.MatchesPerAnte + 1;
        if (args.Length != 1 || !int.TryParse(args[0], out var ante) || ante < 1 || ante > last) throw Usage($"ante <1-{last}>");
        return Match([((ante - 1) * CampaignRun.MatchesPerAnte + 1).ToString()]);
    }

    private async Task<IEnumerable<ConsoleLine>> Shop(string[] args)
    {
        var run = RequireRun(); RequireBetweenMatches();
        var verb = args.FirstOrDefault()?.ToLowerInvariant();
        if (verb is not ("reroll" or "clear")) throw Usage("shop reroll | shop clear");
        if (run.Phase is not CampaignPhase.Shop) throw new DeveloperCommandException("The shop is not open right now.");
        if (verb == "clear")
        {
            await host.UpdateRunAsync(r => r with { Shop = r.Shop! with { Offers = [] } });
            return [Ok("The shop shelves are empty.")];
        }
        // Clearing the stock makes the shop roll fresh offers when it is shown again.
        await host.UpdateRunAsync(r => r with { Shop = null });
        return [Ok("The shop has been restocked.")];
    }

    private async Task<IEnumerable<ConsoleLine>> Score(string[] args)
    {
        if (!host.InMatch) throw new DeveloperCommandException("Scores only exist during a match.");
        if (args.Length != 2 || !int.TryParse(args[0], out var us) || !int.TryParse(args[1], out var them)) throw Usage("score <us> <them>");
        await host.SetScoresAsync(us, them);
        return [Ok($"Score set to {us}–{them}. The match ends when the current hand is scored.")];
    }

    private async Task<IEnumerable<ConsoleLine>> God(string[] args)
    {
        RequireRun();
        await host.UpdateResourcesAsync(s =>
        {
            var wallets = s.Wallets.ToArray(); wallets[0] = 999;
            return s with { Wallets = wallets, Inventory = s.Inventory with { Unlocks = Enum.GetValues<GumballId>() } };
        });
        var lines = new List<ConsoleLine> { Ok("$999 and every bid unlocked.") };
        if (!host.InMatch) { await host.UpdateRunAsync(r => r with { Lives = Math.Max(r.Lives, 5) }); lines.Add(Ok("Lives topped up to 5.")); }
        return lines;
    }

    // ---- helpers ----

    private Task Inventory(Func<CampaignInventory, CampaignInventory> update) =>
        host.UpdateResourcesAsync(s => s with { Inventory = update(s.Inventory.Copy()) });

    private CampaignRun RequireRun() => host.Run ?? throw new DeveloperCommandException("No campaign run is loaded. Open Campaign first.");

    private void RequireBetweenMatches()
    {
        if (host.InMatch) throw new DeveloperCommandException("That only works between matches. Use 'win' or 'lose' to end this one.");
    }

    private int Wallet(CampaignRun run) => host.InMatch ? run.Resources.Wallets[0] : run.Money;

    private static int Adjust(int current, string text)
    {
        var relative = text.StartsWith('+') || text.StartsWith('-');
        if (!int.TryParse(text.TrimStart('$', '='), out var number)) throw new DeveloperCommandException($"'{text}' is not a number.");
        return relative ? current + number : number;
    }

    private static string Rest(string[] args, int from) => string.Join(' ', args[from..]);

    private static string Name(FeatureCategory category, string key) => FeatureCatalog.Get(category, key).Name;

    private static T Resolve<T>(FeatureCategory category, string query) where T : struct, Enum => Enum.Parse<T>(ResolveKey(category, query));

    /// <summary>Finds a catalog key by exact key or name, then by unique prefix, then by unique substring, ignoring case and punctuation.</summary>
    public static string ResolveKey(FeatureCategory category, string query)
    {
        static string Norm(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var q = Norm(query);
        if (q.Length == 0) throw new DeveloperCommandException("Name something.");
        var entries = FeatureCatalog.All.Where(f => f.Category == category).ToArray();
        FeatureDefinition[] Pick(Func<string, bool> test) => entries.Where(f => test(Norm(f.Key)) || test(Norm(f.Name))).ToArray();
        foreach (var matches in new[] { Pick(n => n == q), Pick(n => n.StartsWith(q)), Pick(n => n.Contains(q)) })
        {
            if (matches.Length == 1) return matches[0].Key;
            if (matches.Length > 1)
                throw new DeveloperCommandException($"'{query}' could be {string.Join(", ", matches.Select(m => m.Name))}.");
        }
        throw new DeveloperCommandException($"No {category.ToString().ToLowerInvariant()} called '{query}'. Try 'list {category.ToString().ToLowerInvariant()}s'.");
    }

    private static ConsoleLine Ok(string text) => new(text, ConsoleTone.Ok);
    private static ConsoleLine Error(string text) => new(text, ConsoleTone.Error);
    private static DeveloperCommandException Usage(string usage) => new($"Usage: {usage}");
}

public sealed class DeveloperCommandException(string message) : Exception(message);
