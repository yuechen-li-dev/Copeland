namespace Copeland.Markdown;

public enum MarkdownDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record MarkdownDiagnostic(
    string Id,
    string Message,
    MarkdownDiagnosticSeverity Severity,
    SourceSpan Span);

public sealed record DocumentDiagnostic(
    string Id,
    string Message,
    MarkdownDiagnosticSeverity Severity,
    SourceSpan Span);
