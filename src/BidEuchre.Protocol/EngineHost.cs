namespace BidEuchre.Protocol;

public interface IBidEuchreBot
{
    string Name { get; }
    string Author { get; }
    ValueTask NewGameAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    ValueTask SetOptionAsync(string name, string? value, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    ValueTask<BotAction> ChooseActionAsync(BotPosition position, CancellationToken cancellationToken = default);
}

public sealed class EngineHost
{
    private readonly IBidEuchreBot _bot;
    private readonly CommandRouter _router;
    private BotPosition? _position;
    private bool _quit;

    public EngineHost(IBidEuchreBot bot)
    {
        _bot = bot;
        _router = new CommandRouter()
            .Register("beuci", IdentifyAsync)
            .Register("isready", context => context.WriteLineAsync("readyok"))
            .Register("newgame", NewGameAsync)
            .Register("setoption", SetOptionAsync)
            .Register("position", PositionAsync)
            .Register("go", GoAsync)
            .Register("stop", _ => Task.CompletedTask)
            .Register("quit", QuitAsync);
    }

    public async Task RunAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        while (!_quit && !cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            await _router.DispatchAsync(line, output, cancellationToken);
        }
    }

    private async Task IdentifyAsync(CommandContext context)
    {
        await context.WriteLineAsync($"id name {CommandTokenizer.Quote(_bot.Name)}");
        await context.WriteLineAsync($"id author {CommandTokenizer.Quote(_bot.Author)}");
        await context.WriteLineAsync("protocol bideuchre 1");
        await context.WriteLineAsync("beuciok");
    }

    private async Task NewGameAsync(CommandContext context)
    {
        _position = null;
        await _bot.NewGameAsync(context.CancellationToken);
    }

    private async Task SetOptionAsync(CommandContext context)
    {
        var nameIndex = IndexOf(context.Arguments, "name");
        var valueIndex = IndexOf(context.Arguments, "value");
        if (nameIndex < 0 || nameIndex + 1 >= context.Arguments.Count)
        {
            throw new ProtocolException("setoption requires 'name <name>' and optionally 'value <value>'.");
        }

        var nameEnd = valueIndex > nameIndex ? valueIndex : context.Arguments.Count;
        var name = string.Join(' ', context.Arguments.Skip(nameIndex + 1).Take(nameEnd - nameIndex - 1));
        var value = valueIndex >= 0 ? string.Join(' ', context.Arguments.Skip(valueIndex + 1)) : null;
        await _bot.SetOptionAsync(name, value, context.CancellationToken);
    }

    private Task PositionAsync(CommandContext context)
    {
        if (context.Arguments.Count != 1)
        {
            throw new ProtocolException("position requires one base64url payload.");
        }

        _position = PositionCodec.Decode(context.Arguments[0]);
        return Task.CompletedTask;
    }

    private async Task GoAsync(CommandContext context)
    {
        if (_position is null)
        {
            throw new ProtocolException("A position must be supplied before go.");
        }

        var action = await _bot.ChooseActionAsync(_position, context.CancellationToken);
        await context.WriteLineAsync(BotActionNotation.Format(action));
    }

    private Task QuitAsync(CommandContext context)
    {
        _quit = true;
        return Task.CompletedTask;
    }

    private static int IndexOf(IReadOnlyList<string> arguments, string value)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index].Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
