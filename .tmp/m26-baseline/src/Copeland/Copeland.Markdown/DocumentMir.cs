namespace Copeland.Markdown;

public sealed record DocumentMir(
    IReadOnlyList<DocumentBlockMir> Blocks,
    IReadOnlyList<DocumentDiagnostic> Diagnostics)
{
    /// <summary>
    /// Binding facts are deliberately separate from syntax. Every document
    /// frontend assigns this metadata before any consumer observes the MIR.
    /// </summary>
    public DocumentMetadata Metadata { get; init; } = DocumentMetadata.Unbound;
}

public enum DocumentSourceKind
{
    Markdown,
    TextXml,
    TextPlain,
}

public sealed record DocumentProvenance(
    DocumentSourceKind SourceKind,
    string SourcePath,
    int Start,
    int Length)
{
    public int End => Start + Length;
}

public sealed record DocumentMetadata(
    string DocumentId,
    string? OwnerSymbol,
    DocumentProvenance Provenance)
{
    public static DocumentMetadata Unbound { get; } = new(
        "<unbound-document>",
        null,
        new DocumentProvenance(DocumentSourceKind.Markdown, "<memory>", 0, 0));
}

public sealed record DocumentNodeMetadata(
    string NodeId,
    string? ParentNodeId,
    int AuthoredOrder,
    string? Role,
    DocumentProvenance Provenance)
{
    public static DocumentNodeMetadata Unbound { get; } = new(
        "<unbound-node>",
        null,
        0,
        null,
        new DocumentProvenance(DocumentSourceKind.Markdown, "<memory>", 0, 0));
}

public enum DocumentListKind
{
    Bullet,
    Ordered,
}

public abstract record DocumentBlockMir(SourceSpan Span)
{
    public DocumentNodeMetadata Metadata { get; init; } = DocumentNodeMetadata.Unbound;
}

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
    SourceSpan Span)
{
    public DocumentNodeMetadata Metadata { get; init; } = DocumentNodeMetadata.Unbound;
    public IReadOnlyList<DocumentBlockMir> ChildBlocks { get; init; } = [];
}

public sealed record CodeBlockMir(
    string? Language,
    string Text,
    SourceSpan Span) : DocumentBlockMir(Span);

public sealed record ThematicBreakMir(SourceSpan Span) : DocumentBlockMir(Span);

public sealed record QuoteMir(
    IReadOnlyList<DocumentInlineMir> Inlines,
    SourceSpan Span) : DocumentBlockMir(Span);

public sealed record CalloutMir(
    IReadOnlyList<DocumentInlineMir> Inlines,
    SourceSpan Span) : DocumentBlockMir(Span);

public sealed record BreakMir(SourceSpan Span) : DocumentBlockMir(Span);

public abstract record DocumentInlineMir(SourceSpan Span)
{
    public DocumentNodeMetadata Metadata { get; init; } = DocumentNodeMetadata.Unbound;
}

public sealed record TextMir(string Text, SourceSpan Span) : DocumentInlineMir(Span);

/// <summary>
/// A typed expression slot authored in Text TS-XML. The canonical document
/// model owns its place and provenance; ordinary semantic binding owns the
/// expression selected by <see cref="SlotId"/>.
/// </summary>
public sealed record EmbeddedValueMir(string SlotId, SourceSpan Span) : DocumentInlineMir(Span);

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
