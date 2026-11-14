using BidEuchre.App;

internal static class MarkdownDocumentTests
{
    private static string RulesFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "bid-euchre-final-rules.md"))) return File.ReadAllText(Path.Combine(directory.FullName, "bid-euchre-final-rules.md"));
        throw new FileNotFoundException("bid-euchre-final-rules.md");
    }

    public static void ParsesBlocks()
    {
        var blocks = MarkdownDocument.Parse("""
            # Title

            Intro that wraps
            onto two lines.

            - First item that
              continues here.
            - Second item.

            1. Step one.
            2. Step two
               continues.

            ```text
            3 < 4
            ```

            | Bid | Score |
            | --- | ---: |
            | 4 | +4 |
            | 4 | -4 |
            """);
        Assert.Equal(6, blocks.Count);
        Assert.Equal(new MarkdownHeading(1, "Title"), (MarkdownHeading)blocks[0]);
        Assert.Equal("Intro that wraps onto two lines.", ((MarkdownParagraph)blocks[1]).Text);
        var bullets = (MarkdownList)blocks[2];
        Assert.False(bullets.Ordered); Assert.Equal("First item that continues here.|Second item.", string.Join('|', bullets.Items));
        var steps = (MarkdownList)blocks[3];
        Assert.True(steps.Ordered); Assert.Equal("Step one.|Step two continues.", string.Join('|', steps.Items));
        Assert.Equal("3 < 4", ((MarkdownCode)blocks[4]).Text);
        var table = (MarkdownTable)blocks[5];
        Assert.Equal("Bid|Score", string.Join('|', table.Header));
        Assert.Equal("False|True", string.Join('|', table.RightAligned));
        Assert.Equal("4,-4", string.Join(',', table.Rows[1]));
    }

    public static void ParsesInlineSpans()
    {
        var spans = MarkdownDocument.Inline("does **not** score `-4` here");
        Assert.Equal(5, spans.Count);
        Assert.Equal(new MarkdownSpan("not", Bold: true), spans[1]);
        Assert.Equal(new MarkdownSpan("-4", Code: true), spans[3]);
        Assert.Equal(new MarkdownSpan("a ` alone"), MarkdownDocument.Inline("a ` alone").Single());
    }

    // Issue #15: none of the rules file's Markdown syntax may reach the page as text.
    public static void RulesFileHasNoRawSyntax()
    {
        var blocks = MarkdownDocument.Parse(RulesFile());
        Assert.True(blocks.OfType<MarkdownTable>().Count() >= 2, "The rules file's tables are parsed as tables.");
        Assert.True(blocks.OfType<MarkdownCode>().Any(), "The rules file's code fences are parsed as code.");
        var text = blocks.SelectMany(block => block switch
        {
            MarkdownHeading h => [h.Text], MarkdownParagraph p => [p.Text], MarkdownList l => l.Items,
            MarkdownTable t => t.Header.Concat(t.Rows.SelectMany(r => r)), _ => []
        }).ToArray();
        foreach (var line in text)
            Assert.False(line.StartsWith('#') || line.Contains('|') || line.Contains("```") || line.StartsWith("- "), $"Raw Markdown left in: {line}");
        foreach (var span in text.SelectMany(MarkdownDocument.Inline))
            Assert.False(span.Text.Contains("**") || span.Text.Contains('`'), $"Raw inline Markdown left in: {span.Text}");
    }
}
