using System.Collections.Concurrent;
using Copeland.Markdown;

namespace Oblivion.Product;

public sealed record OblivionMarkdownProjection(
    string Source,
    string? SourceReference,
    DocumentMir? Document,
    IReadOnlyList<string> Preview,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics);

public static class OblivionMarkdownBody
{
    private static readonly ConcurrentDictionary<OblivionCardBody, OblivionMarkdownProjection> Cache = new();

    public static OblivionCardBody CreatePlain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new OblivionCardBody(
            OblivionCardBodyFormat.Plain,
            new OblivionPlainTextContent(text));
    }

    public static OblivionCardBody CreateMarkdown(string markdownText, string? sourceReference = null)
    {
        ArgumentNullException.ThrowIfNull(markdownText);

        OblivionCardContent content = string.IsNullOrWhiteSpace(sourceReference)
            ? new OblivionInlineMarkdownContent(markdownText)
            : new OblivionMarkdownReferenceContent(markdownText, sourceReference);
        return new OblivionCardBody(OblivionCardBodyFormat.CopelandMarkdown, content);
    }

    public static OblivionMarkdownProjection Project(OblivionCardBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Cache.GetOrAdd(body, CreateProjection);
    }

    public static void ClearProjectionCache()
    {
        Cache.Clear();
    }

    public static IReadOnlyList<string> BuildInspectorLines(OblivionCardBody body)
    {
        return OblivionMarkdownRenderer.BuildInspectorLines(body);
    }

    public static IReadOnlyList<string> BuildPreviewLines(
        DocumentMir mir,
        IReadOnlyList<OblivionCardDiagnostic> diagnostics)
    {
        return OblivionMarkdownRenderer.BuildPreviewLines(mir, diagnostics);
    }

    public static IReadOnlyList<string> BuildDiagnosticLines(
        IReadOnlyList<OblivionCardDiagnostic> diagnostics)
    {
        return OblivionMarkdownRenderer.BuildDiagnosticLines(diagnostics);
    }

    public static string RenderInlineList(IReadOnlyList<DocumentInlineMir> inlines)
    {
        return OblivionMarkdownRenderer.RenderInlineList(inlines);
    }

    private static OblivionMarkdownProjection CreateProjection(OblivionCardBody body)
    {
        if (body.Format == OblivionCardBodyFormat.Plain)
        {
            return new OblivionMarkdownProjection(
                body.RawText,
                body.SourceReference,
                Document: null,
                Preview: SplitLines(body.RawText),
                Diagnostics: []);
        }

        MarkdownCompilation compilation = MarkdownCompiler.Compile(body.RawText);
        IReadOnlyList<OblivionCardDiagnostic> diagnostics = compilation.Mir.Diagnostics
            .Select(diagnostic => new OblivionCardDiagnostic(
                diagnostic.Id,
                ParseSeverity(diagnostic.Severity.ToString()),
                diagnostic.Message,
                body.SourceReference,
                diagnostic.Span.StartLocation.Line,
                diagnostic.Span.StartLocation.Column))
            .ToArray();

        return new OblivionMarkdownProjection(
            body.RawText,
            body.SourceReference,
            compilation.Mir,
            OblivionMarkdownRenderer.BuildPreviewLines(compilation.Mir, diagnostics),
            diagnostics);
    }

    private static OblivionCardDiagnosticSeverity ParseSeverity(string severity)
    {
        return severity.Equals("Error", StringComparison.OrdinalIgnoreCase)
            ? OblivionCardDiagnosticSeverity.Error
            : severity.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                ? OblivionCardDiagnosticSeverity.Warning
                : OblivionCardDiagnosticSeverity.Info;
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToArray();
    }
}
