namespace Copeland.Markdown;

public sealed record DocumentMir(
    IReadOnlyList<DocumentBlockMir> Blocks,
    IReadOnlyList<DocumentDiagnostic> Diagnostics);

public enum DocumentListKind
{
    Bullet,
    Ordered,
}

public abstract record DocumentBlockMir(SourceSpan Span);

public sealed record HeadingMir(
    int Level,
    IReadOnlyList<DocumentInlineMir> Inlines,
    SourceSpan Span) : DocumentBlockMir(Span);

public sealed record ParagraphMir(
    IReadOnlyList<DocumentInlineMir> Inlines,
    SourceSpan Span) : DocumentBlockMir(Span);

public sealed record ListMir(
    DocumentListKind Kind,
    IReadOnlyList<ListItemMir> Items,
    SourceSpan Span) : DocumentBlockMir(Span);

public sealed record ListItemMir(
    IReadOnlyList<DocumentInlineMir> Inlines,
    SourceSpan Span);

public sealed record CodeBlockMir(
    string? Language,
    string Text,
    SourceSpan Span) : DocumentBlockMir(Span);

public sealed record ThematicBreakMir(SourceSpan Span) : DocumentBlockMir(Span);

public abstract record DocumentInlineMir(SourceSpan Span);

public sealed record TextMir(string Text, SourceSpan Span) : DocumentInlineMir(Span);

public sealed record CodeSpanMir(string Text, SourceSpan Span) : DocumentInlineMir(Span);

public sealed record EmphasisMir(
    IReadOnlyList<DocumentInlineMir> Children,
    SourceSpan Span) : DocumentInlineMir(Span);

public sealed record StrongMir(
    IReadOnlyList<DocumentInlineMir> Children,
    SourceSpan Span) : DocumentInlineMir(Span);

public sealed record LinkMir(
    IReadOnlyList<DocumentInlineMir> Label,
    string Target,
    SourceSpan Span) : DocumentInlineMir(Span);
