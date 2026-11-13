using BidEuchre.App;
using BidEuchre.Core;

internal static class HandOrderTests
{
    private static string Sorted(string hand, HandSort sort, Contract? contract = null) =>
        string.Join(' ', HandOrder.Sort(hand.Split(' ').Select(Card.Parse), card => card, sort, contract).Select(card => card.Code));

    public static void BySuit()
    {
        Check(Sorted("9C AS KH TD JS QH", HandSort.Suit) == "AS JS KH QH 9C TD",
            "Suits should alternate colours, high to low within each");
        var hearts = Contract.Create(BidLevel.Four, ContractMode.Trump, Suit.Hearts);
        Check(Sorted("AS JD 9H AH JH KC", HandSort.Suit, hearts) == "JH JD AH 9H KC AS",
            "Trump should lead with both bowers on top, and the Left Bower should leave its printed suit");
        var high = Contract.Create(BidLevel.Four, ContractMode.High, null);
        Check(Sorted("JD AH JH AD", HandSort.Suit, high) == "AH JH AD JD", "A no-trump contract has no bowers");
    }

    public static void ByRank()
    {
        Check(Sorted("9C AS KH TD JS AH", HandSort.Rank) == "AS AH KH JS TD 9C",
            "Ranks should run high to low, with equal ranks in suit order");
        var clubs = Contract.Create(BidLevel.Four, ContractMode.Trump, Suit.Clubs);
        Check(Sorted("AS JS AC JC 9C JH", HandSort.Rank, clubs) == "JC JS AC AS JH 9C",
            "Bowers outrank aces once trump is named, and trump breaks ties");
    }

    public static void KeepsDuplicatesStable()
    {
        var hand = new[] { (Id: 1, Face: Card.Parse("AS")), (Id: 2, Face: Card.Parse("KH")), (Id: 3, Face: Card.Parse("AS")) };
        var ids = HandOrder.Sort(hand, card => card.Face, HandSort.Suit, null).Select(card => card.Id).ToArray();
        Check(ids.SequenceEqual([1, 3, 2]), "Identical campaign cards must keep their relative order");
    }

    public static void Persists()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"euchre-preferences-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "preferences.json");
        try
        {
            Check(GamePreferences.Load(path).HandSort == HandSort.Suit, "Hands should sort by suit by default");
            Check((new GamePreferences() with { HandSort = HandSort.Rank }).Save(path), "The sort choice should save");
            Check(GamePreferences.Load(path).HandSort == HandSort.Rank, "The sort choice should survive restart");
            File.WriteAllText(path, "{\"HandSort\":7}");
            Check(GamePreferences.Load(path).HandSort == HandSort.Suit, "An unknown sort should fall back to suit");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
