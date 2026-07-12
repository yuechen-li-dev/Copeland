using Copeland.Markdown;

namespace Machina.Presenter.Sample;

public static class OblivionMarkdownBody
{
    public static OblivionCardBody CreatePlain(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new OblivionCardBody(
            OblivionCardBodyFormat.Plain,
            text,
            BodySourcePath: null,
            PreviewLines: SplitLines(text),
            DocumentMir: null,
            Diagnostics: []);
    }

    public static OblivionCardBody CreateMarkdown(
        string markdownText,
        string? bodySourcePath,
        MarkdownCompilation compilation,
        IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(markdownText);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return new OblivionCardBody(
            OblivionCardBodyFormat.CopelandMarkdown,
            markdownText,
            bodySourcePath,
            PreviewLines: BuildPreviewLines(compilation.Mir, diagnostics),
            DocumentMir: compilation.Mir,
            Diagnostics: diagnostics);
    }

    public static IReadOnlyList<string> BuildInspectorLines(OblivionCardBody body)
    {
        return OblivionMarkdownRenderer.BuildInspectorLines(body);
    }

    public static IReadOnlyList<string> BuildPreviewLines(DocumentMir mir, IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        return OblivionMarkdownRenderer.BuildPreviewLines(mir, diagnostics);
    }

    public static IReadOnlyList<string> BuildDiagnosticLines(IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        return OblivionMarkdownRenderer.BuildDiagnosticLines(diagnostics);
    }

    public static string RenderInlineList(IReadOnlyList<DocumentInlineMir> inlines)
    {
        return OblivionMarkdownRenderer.RenderInlineList(inlines);
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToArray();
    }
}
