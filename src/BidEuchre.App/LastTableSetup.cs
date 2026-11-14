namespace BidEuchre.App;

public sealed record LastTableSetup(string TableName, int BotDelayMilliseconds, SeatConfiguration[] Seats)
{
    public static LastTableSetup Default => new("Friday Table", 1000,
        [new(0, "You", PlayerKind.Human, null), new(1, "TableBot West", PlayerKind.Bot, EngineCatalog.BuiltInEngineId),
         new(2, "TableBot North", PlayerKind.Bot, EngineCatalog.BuiltInEngineId), new(3, "TableBot East", PlayerKind.Bot, EngineCatalog.BuiltInEngineId)]);

    public LastTableSetup Normalized()
    {
        var defaults = Default;
        var seats = Enumerable.Range(0, 4).Select(index =>
        {
            var seat = Seats?.FirstOrDefault(candidate => candidate is not null && candidate.Seat == index) ?? defaults.Seats[index];
            var kind = Enum.IsDefined(seat.Kind) ? seat.Kind : defaults.Seats[index].Kind;
            return new SeatConfiguration(index, string.IsNullOrWhiteSpace(seat.Name) ? defaults.Seats[index].Name : seat.Name.Trim(), kind,
                kind == PlayerKind.Human ? null : string.IsNullOrWhiteSpace(seat.EngineId) ? EngineCatalog.BuiltInEngineId : seat.EngineId);
        }).ToArray();
        return new(string.IsNullOrWhiteSpace(TableName) ? defaults.TableName : TableName.Trim(),
            BotDelayMilliseconds is 600 or 1000 or 1500 ? BotDelayMilliseconds : 1000, seats);
    }

    public static LastTableSetup Load(string path) => (LocalJsonFile.Load<LastTableSetup>(path) ?? Default).Normalized();
    public bool Save(string path) => LocalJsonFile.Save(path, Normalized());
}
