namespace Copeland.Markdown;

public sealed record MarkdownDocument(
    IReadOnlyList<MarkdownBlock> Blocks,
    IReadOnlyList<MarkdownDiagnostic> Diagnostics,
    SourceSpan Span);

public abstract record MarkdownBlock(SourceSpan Span);

public sealed record HeadingBlock(
    int Level,
    IReadOnlyList<MarkdownInline> Inlines,
    SourceSpan Span) : MarkdownBlock(Span);

public sealed record ParagraphBlock(
    IReadOnlyList<MarkdownInline> Inlines,
    SourceSpan Span) : MarkdownBlock(Span);

public sealed record BulletListBlock(
    IReadOnlyList<ListItemBlock> Items,
    SourceSpan Span) : MarkdownBlock(Span);

public sealed record OrderedListBlock(
    IReadOnlyList<ListItemBlock> Items,
    SourceSpan Span) : MarkdownBlock(Span);

public sealed record ListItemBlock(
    IReadOnlyList<MarkdownInline> Inlines,
    SourceSpan Span);

public sealed record CodeFenceBlock(
    string? Language,
    string Text,
    SourceSpan Span) : MarkdownBlock(Span);

public sealed record ThematicBreakBlock(SourceSpan Span) : MarkdownBlock(Span);

public abstract record MarkdownInline(SourceSpan Span);

public sealed record TextInline(string Text, SourceSpan Span) : MarkdownInline(Span);

public sealed record CodeInline(string Text, SourceSpan Span) : MarkdownInline(Span);

public sealed record EmphasisInline(
    IReadOnlyList<MarkdownInline> Children,
    SourceSpan Span) : MarkdownInline(Span);

public sealed record StrongInline(
    IReadOnlyList<MarkdownInline> Children,
    SourceSpan Span) : MarkdownInline(Span);

public sealed record LinkInline(
    IReadOnlyList<MarkdownInline> Label,
    string Target,
    SourceSpan Span) : MarkdownInline(Span);
