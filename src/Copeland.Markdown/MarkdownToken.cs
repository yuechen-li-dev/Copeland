namespace Copeland.Markdown;

public enum MarkdownTokenKind
{
    Text,
    WhiteSpace,
    Hash,
    Star,
    Dash,
    Backtick,
    OpenBracket,
    CloseBracket,
    OpenParen,
    CloseParen,
    Dot,
    GreaterThan,
    Exclamation,
    NewLine,
    EndOfFile,
}

public sealed record MarkdownToken(
    MarkdownTokenKind Kind,
    string Text,
    SourceSpan Span);

public sealed record MarkdownLine(
    int LineNumber,
    string Text,
    SourceSpan Span,
    IReadOnlyList<MarkdownToken> Tokens)
{
    public bool IsBlank => string.IsNullOrWhiteSpace(Text);
}

public sealed record MarkdownTokenizedSource(
    MarkdownSourceText Source,
    IReadOnlyList<MarkdownLine> Lines,
    IReadOnlyList<MarkdownToken> Tokens);
