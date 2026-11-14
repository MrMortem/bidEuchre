namespace BidEuchre.App;

public abstract record MarkdownBlock;
public sealed record MarkdownHeading(int Level, string Text) : MarkdownBlock;
public sealed record MarkdownParagraph(string Text) : MarkdownBlock;
public sealed record MarkdownList(bool Ordered, IReadOnlyList<string> Items) : MarkdownBlock;
public sealed record MarkdownCode(string Text) : MarkdownBlock;
/// <param name="RightAligned">Per column, whether its separator was written <c>---:</c>.</param>
public sealed record MarkdownTable(IReadOnlyList<string> Header, IReadOnlyList<bool> RightAligned, IReadOnlyList<IReadOnlyList<string>> Rows) : MarkdownBlock;
public sealed record MarkdownSpan(string Text, bool Bold = false, bool Code = false);

/// <summary>The small Markdown subset the rules file uses: headings, paragraphs, bullet and numbered
/// lists (with indented continuation lines), fenced code, pipe tables, <c>**bold**</c> and <c>`code`</c>.</summary>
public static class MarkdownDocument
{
    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        var blocks = new List<MarkdownBlock>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        List<string>? paragraph = null;
        (bool Ordered, List<string> Items)? list = null;

        void Flush()
        {
            if (paragraph is not null) blocks.Add(new MarkdownParagraph(string.Join(' ', paragraph)));
            if (list is { } open) blocks.Add(new MarkdownList(open.Ordered, open.Items));
            paragraph = null; list = null;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.TrimStart().StartsWith("```"))
            {
                Flush();
                var code = new List<string>();
                while (++i < lines.Length && !lines[i].TrimStart().StartsWith("```")) code.Add(lines[i].TrimEnd());
                blocks.Add(new MarkdownCode(string.Join('\n', code)));
                continue;
            }
            if (line.Length == 0) { Flush(); continue; }

            var trimmed = line.TrimStart();
            var heading = trimmed.TakeWhile(c => c == '#').Count();
            if (heading is >= 1 and <= 6 && trimmed.Length > heading && trimmed[heading] == ' ')
            {
                Flush();
                blocks.Add(new MarkdownHeading(heading, trimmed[(heading + 1)..].Trim()));
                continue;
            }
            if (trimmed.StartsWith('|') && i + 1 < lines.Length && IsSeparator(lines[i + 1]))
            {
                Flush();
                var header = Cells(trimmed);
                var right = Cells(lines[++i]).Select(cell => cell.EndsWith(':')).ToArray();
                var rows = new List<IReadOnlyList<string>>();
                while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith('|')) rows.Add(Cells(lines[++i]));
                blocks.Add(new MarkdownTable(header, right, rows));
                continue;
            }
            if (Item(trimmed) is { } item)
            {
                if (paragraph is not null || list is { } other && other.Ordered != item.Ordered) Flush();
                list ??= (item.Ordered, []);
                list.Value.Items.Add(item.Text);
                continue;
            }
            // An indented line under a list item continues that item.
            if (list is { } current && line.Length > trimmed.Length)
            {
                current.Items[^1] += ' ' + trimmed;
                continue;
            }
            if (list is not null) Flush();
            (paragraph ??= []).Add(trimmed);
        }
        Flush();
        return blocks;
    }

    public static IReadOnlyList<MarkdownSpan> Inline(string text)
    {
        var spans = new List<MarkdownSpan>();
        var plain = new System.Text.StringBuilder();
        void Plain() { if (plain.Length > 0) { spans.Add(new(plain.ToString())); plain.Clear(); } }
        for (var i = 0; i < text.Length;)
        {
            if (text[i] == '`' && text.IndexOf('`', i + 1) is var endCode and > 0)
            { Plain(); spans.Add(new(text[(i + 1)..endCode], Code: true)); i = endCode + 1; continue; }
            if (text.AsSpan(i).StartsWith("**") && text.IndexOf("**", i + 2, StringComparison.Ordinal) is var endBold and > 0)
            { Plain(); spans.Add(new(text[(i + 2)..endBold], Bold: true)); i = endBold + 2; continue; }
            plain.Append(text[i++]);
        }
        Plain();
        return spans;
    }

    private static (bool Ordered, string Text)? Item(string line)
    {
        if (line.Length > 2 && line[0] is '-' or '*' && line[1] == ' ') return (false, line[2..].Trim());
        var digits = line.TakeWhile(char.IsAsciiDigit).Count();
        if (digits > 0 && line.Length > digits + 2 && line[digits] == '.' && line[digits + 1] == ' ') return (true, line[(digits + 2)..].Trim());
        return null;
    }

    private static bool IsSeparator(string line)
    {
        var cells = Cells(line);
        return line.TrimStart().StartsWith('|') && cells.Count > 0 && cells.All(cell => cell.Trim(':').Length > 0 && cell.Trim(':').All(c => c == '-'));
    }

    private static IReadOnlyList<string> Cells(string line)
    {
        var inner = line.Trim();
        if (inner.StartsWith('|')) inner = inner[1..];
        if (inner.EndsWith('|')) inner = inner[..^1];
        return inner.Split('|').Select(cell => cell.Trim()).ToArray();
    }
}
