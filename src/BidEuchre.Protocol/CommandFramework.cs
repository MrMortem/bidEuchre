using System.Text;

namespace BidEuchre.Protocol;

public sealed class CommandContext(
    string command,
    IReadOnlyList<string> arguments,
    TextWriter output,
    CancellationToken cancellationToken)
{
    public string Command { get; } = command;
    public IReadOnlyList<string> Arguments { get; } = arguments;
    public TextWriter Output { get; } = output;
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public Task WriteLineAsync(string value) => Output.WriteLineAsync(value);
}

public sealed class CommandRouter
{
    private readonly Dictionary<string, Func<CommandContext, Task>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public CommandRouter Register(string name, Func<CommandContext, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[name] = handler;
        return this;
    }

    public async Task<bool> DispatchAsync(string line, TextWriter output, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> tokens;
        try
        {
            tokens = CommandTokenizer.Tokenize(line);
        }
        catch (ProtocolException exception)
        {
            await output.WriteLineAsync($"error {Sanitize(exception.Message)}");
            return false;
        }

        if (tokens.Count is 0)
        {
            return true;
        }

        if (!_handlers.TryGetValue(tokens[0], out var handler))
        {
            await output.WriteLineAsync($"error unknown-command {Sanitize(tokens[0])}");
            return false;
        }

        try
        {
            await handler(new CommandContext(tokens[0], tokens.Skip(1).ToArray(), output, cancellationToken));
            await output.FlushAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is ProtocolException or ArgumentException or InvalidOperationException)
        {
            await output.WriteLineAsync($"error {Sanitize(exception.Message)}");
            await output.FlushAsync(cancellationToken);
            return false;
        }
    }

    private static string Sanitize(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}

public static class CommandTokenizer
{
    public static IReadOnlyList<string> Tokenize(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return [];
        }

        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var escaped = false;

        foreach (var character in line)
        {
            if (escaped)
            {
                current.Append(character switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => character
                });
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                AddToken(result, current);
                continue;
            }

            current.Append(character);
        }

        if (escaped || quoted)
        {
            throw new ProtocolException("Command contains an unfinished escape or quote.");
        }

        AddToken(result, current);
        return result;
    }

    public static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")}\"";

    private static void AddToken(List<string> result, StringBuilder current)
    {
        if (current.Length is 0)
        {
            return;
        }

        result.Add(current.ToString());
        current.Clear();
    }
}
