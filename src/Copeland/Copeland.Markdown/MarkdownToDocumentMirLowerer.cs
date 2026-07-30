namespace Copeland.Markdown;

public static class MarkdownToDocumentMirLowerer
{
    public static DocumentMir Lower(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        IReadOnlyList<DocumentBlockMir> blocks = document.Blocks.Select(LowerBlock).ToArray();
        IReadOnlyList<DocumentDiagnostic> diagnostics = document.Diagnostics
            .Select(static diagnostic => new DocumentDiagnostic(
                diagnostic.Id,
                diagnostic.Message,
                diagnostic.Severity,
                diagnostic.Span))
            .ToArray();

        return new DocumentMir(blocks, diagnostics);
    }

    private static DocumentBlockMir LowerBlock(MarkdownBlock block)
    {
        return block switch
        {
            HeadingBlock heading => new HeadingMir(
                heading.Level,
                LowerInlineList(heading.Inlines),
                heading.Span),
            ParagraphBlock paragraph => new ParagraphMir(
                LowerInlineList(paragraph.Inlines),
                paragraph.Span),
            BulletListBlock bulletList => new ListMir(
                DocumentListKind.Bullet,
                bulletList.Items.Select(LowerListItem).ToArray(),
                bulletList.Span),
            OrderedListBlock orderedList => new ListMir(
                DocumentListKind.Ordered,
                orderedList.Items.Select(LowerListItem).ToArray(),
                orderedList.Span),
            CodeFenceBlock codeFence => new CodeBlockMir(
                codeFence.Language,
                codeFence.Text,
                codeFence.Span),
            ThematicBreakBlock thematicBreak => new ThematicBreakMir(thematicBreak.Span),
            _ => throw new InvalidOperationException($"Unsupported Markdown block type: {block.GetType().Name}"),
        };
    }

    private static ListItemMir LowerListItem(ListItemBlock item)
    {
        return new ListItemMir(LowerInlineList(item.Inlines), item.Span);
    }

    public static IReadOnlyList<DocumentInlineMir> LowerInlineList(IReadOnlyList<MarkdownInline> inlines)
    {
        return inlines.Select(LowerInline).ToArray();
    }

    private static DocumentInlineMir LowerInline(MarkdownInline inline)
    {
        return inline switch
        {
            TextInline text => new TextMir(text.Text, text.Span),
            CodeInline code => new CodeSpanMir(code.Text, code.Span),
            EmphasisInline emphasis => new EmphasisMir(LowerInlineList(emphasis.Children), emphasis.Span),
            StrongInline strong => new StrongMir(LowerInlineList(strong.Children), strong.Span),
            LinkInline link => new LinkMir(LowerInlineList(link.Label), link.Target, link.Span),
            _ => throw new InvalidOperationException($"Unsupported Markdown inline type: {inline.GetType().Name}"),
        };
    }
}
