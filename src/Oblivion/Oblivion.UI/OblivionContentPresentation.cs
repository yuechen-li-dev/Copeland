using Copeland.Markdown;

namespace Oblivion.Product;

public enum OblivionReadingState
{
    Collapsed,
    Expanded,
}

public enum OblivionContentPresentationKind
{
    MarkdownDocument,
    PlainText,
    Code,
    MermaidDiagram,
    PngImage,
    ArtifactMetadata,
    DiagnosticFallback,
}

public enum OblivionContentPresenterKind
{
    AvaloniaReadOnlyDocument,
    AvaloniaReadOnlyCode,
    ExternalMermaidRenderer,
    AvaloniaImage,
    NativeText,
    NativeMetadata,
    DiagnosticFallback,
}

public enum OblivionContentScrollContract
{
    None,
    HostVerticalWhenBounded,
    HostHorizontalAndVerticalWhenBounded,
}

public enum OblivionContentFocusContract
{
    HostRetainsFocus,
    PresenterOwnsSelectionAndCopy,
}

public sealed record OblivionResolvedContentArtifact(
    string ArtifactId,
    string Label,
    string Kind,
    string? DeclaredReference,
    string? ResolvedPath,
    bool Exists,
    string? MediaType,
    bool Generated,
    string? SourceReference);

public sealed record OblivionContentPresentationItem(
    string ContentId,
    OblivionContentPresentationKind ContentKind,
    OblivionContentPresenterKind PresenterKind,
    string Source,
    string? Language,
    string? SourceReference,
    OblivionResolvedContentArtifact? Artifact,
    OblivionContentScrollContract ScrollContract,
    OblivionContentFocusContract FocusContract,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics);

public sealed record OblivionContentPresentationPlan(
    string ContentIdentity,
    OblivionReadingState ReadingState,
    string ContentTypeLabel,
    string CollapsedSummary,
    IReadOnlyList<OblivionContentPresentationItem> Items,
    bool AllowsInternalScroll,
    IReadOnlyList<OblivionCardDiagnostic> Diagnostics);

public sealed record OblivionReadingTypographyBaseline(
    double BodyFontSize,
    double BodyLineHeight,
    IReadOnlyDictionary<int, double> HeadingFontSizes,
    double ParagraphSpacing,
    double ListIndent,
    double CodeFontSize,
    double CodeLineHeight,
    double ContentPadding,
    double CardBodyPadding,
    double InspectorBodyPadding,
    double MaximumReadableWidth)
{
    public static OblivionReadingTypographyBaseline MatureReadOnly { get; } = new(
        BodyFontSize: 16,
        BodyLineHeight: 24,
        HeadingFontSizes: new Dictionary<int, double>
        {
            [1] = 28,
            [2] = 24,
            [3] = 20,
            [4] = 18,
            [5] = 16,
            [6] = 16,
        },
        ParagraphSpacing: 12,
        ListIndent: 24,
        CodeFontSize: 14,
        CodeLineHeight: 20,
        ContentPadding: 16,
        CardBodyPadding: 18,
        InspectorBodyPadding: 16,
        MaximumReadableWidth: 760);
}

public static class OblivionContentPresenterSelector
{
    public static OblivionContentPresentationPlan Select(
        OblivionCard card,
        OblivionCardViewState viewState,
        IReadOnlyList<OblivionResolvedContentArtifact>? resolvedArtifacts = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(viewState);

        OblivionReadingState readingState = viewState.IsExpanded
            ? OblivionReadingState.Expanded
            : OblivionReadingState.Collapsed;
        IReadOnlyList<OblivionResolvedContentArtifact> artifacts = resolvedArtifacts ?? [];
        List<OblivionCardDiagnostic> diagnostics = [];
        List<OblivionContentPresentationItem> items = [];

        OblivionResolvedContentArtifact? png = artifacts.FirstOrDefault(IsPng);
        if (card.Kind == OblivionCardKind.Artifact && png is not null)
        {
            items.Add(CreatePngItem(card, png, diagnostics));
        }
        else if (card.Kind is OblivionCardKind.CodeFact or OblivionCardKind.CodeTheory)
        {
            items.Add(CreateCodeItem(card));
        }
        else if (card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown)
        {
            AddMarkdownItems(card, items, diagnostics);
        }
        else if (card.Kind == OblivionCardKind.Artifact)
        {
            items.Add(CreateArtifactMetadataItem(card, artifacts));
        }
        else
        {
            items.Add(CreatePlainTextItem(card));
        }

        if (items.Count == 0)
        {
            OblivionCardDiagnostic diagnostic = new(
                "OBLIVION-CONTENT-PRESENTER-NOT-FOUND",
                OblivionDiagnosticSeverity.Warning,
                $"No content presenter matched card '{card.Id.Value}'.",
                card.Provenance.SourceReference);
            diagnostics.Add(diagnostic);
            items.Add(new OblivionContentPresentationItem(
                $"{card.Id.Value}.fallback",
                OblivionContentPresentationKind.DiagnosticFallback,
                OblivionContentPresenterKind.DiagnosticFallback,
                diagnostic.Message,
                Language: null,
                card.Body.SourceReference,
                Artifact: null,
                OblivionContentScrollContract.None,
                OblivionContentFocusContract.HostRetainsFocus,
                [diagnostic]));
        }

        return new OblivionContentPresentationPlan(
            ContentIdentity: card.Id.Value,
            readingState,
            ContentTypeLabel: BuildContentTypeLabel(items),
            CollapsedSummary: BuildCollapsedSummary(card),
            Items: items,
            AllowsInternalScroll: readingState == OblivionReadingState.Expanded &&
                items.Any(item => item.ScrollContract != OblivionContentScrollContract.None),
            Diagnostics: diagnostics);
    }

    private static void AddMarkdownItems(
        OblivionCard card,
        List<OblivionContentPresentationItem> items,
        List<OblivionCardDiagnostic> diagnostics)
    {
        OblivionMarkdownProjection projection = OblivionMarkdownBody.Project(card.Body);
        diagnostics.AddRange(projection.Diagnostics);
        items.Add(new OblivionContentPresentationItem(
            $"{card.Id.Value}.markdown",
            OblivionContentPresentationKind.MarkdownDocument,
            OblivionContentPresenterKind.AvaloniaReadOnlyDocument,
            projection.Source,
            Language: "markdown",
            projection.SourceReference,
            Artifact: null,
            OblivionContentScrollContract.HostVerticalWhenBounded,
            OblivionContentFocusContract.PresenterOwnsSelectionAndCopy,
            projection.Diagnostics));

        if (projection.Document is null)
        {
            return;
        }

        int diagramIndex = 0;
        foreach (CodeBlockMir codeBlock in projection.Document.Blocks.OfType<CodeBlockMir>())
        {
            if (!string.Equals(codeBlock.Language, "mermaid", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new OblivionContentPresentationItem(
                $"{card.Id.Value}.mermaid-{diagramIndex}",
                OblivionContentPresentationKind.MermaidDiagram,
                OblivionContentPresenterKind.ExternalMermaidRenderer,
                codeBlock.Text,
                Language: "mermaid",
                projection.SourceReference,
                Artifact: null,
                OblivionContentScrollContract.HostVerticalWhenBounded,
                OblivionContentFocusContract.HostRetainsFocus,
                []));
            diagramIndex++;
        }
    }

    private static OblivionContentPresentationItem CreatePngItem(
        OblivionCard card,
        OblivionResolvedContentArtifact png,
        List<OblivionCardDiagnostic> diagnostics)
    {
        if (!png.Exists)
        {
            diagnostics.Add(new OblivionCardDiagnostic(
                "OBLIVION-CONTENT-PNG-NOT-FOUND",
                OblivionDiagnosticSeverity.Warning,
                $"PNG artifact '{png.ArtifactId}' is not available for inline presentation.",
                png.SourceReference));
        }

        return new OblivionContentPresentationItem(
            $"{card.Id.Value}.png-{png.ArtifactId}",
            OblivionContentPresentationKind.PngImage,
            png.Exists
                ? OblivionContentPresenterKind.AvaloniaImage
                : OblivionContentPresenterKind.DiagnosticFallback,
            png.ResolvedPath ?? png.DeclaredReference ?? string.Empty,
            Language: null,
            png.SourceReference,
            png,
            OblivionContentScrollContract.HostVerticalWhenBounded,
            OblivionContentFocusContract.HostRetainsFocus,
            diagnostics.ToArray());
    }

    private static OblivionContentPresentationItem CreateCodeItem(OblivionCard card)
    {
        string? language = InferLanguage(card);
        return new OblivionContentPresentationItem(
            $"{card.Id.Value}.code",
            OblivionContentPresentationKind.Code,
            OblivionContentPresenterKind.AvaloniaReadOnlyCode,
            card.Body.RawText,
            language,
            card.Body.SourceReference ?? card.Provenance.SourceReference,
            Artifact: null,
            OblivionContentScrollContract.HostHorizontalAndVerticalWhenBounded,
            OblivionContentFocusContract.PresenterOwnsSelectionAndCopy,
            []);
    }

    private static OblivionContentPresentationItem CreatePlainTextItem(OblivionCard card)
    {
        return new OblivionContentPresentationItem(
            $"{card.Id.Value}.plain",
            OblivionContentPresentationKind.PlainText,
            OblivionContentPresenterKind.NativeText,
            card.Body.RawText,
            Language: null,
            card.Body.SourceReference,
            Artifact: null,
            OblivionContentScrollContract.HostVerticalWhenBounded,
            OblivionContentFocusContract.PresenterOwnsSelectionAndCopy,
            []);
    }

    private static OblivionContentPresentationItem CreateArtifactMetadataItem(
        OblivionCard card,
        IReadOnlyList<OblivionResolvedContentArtifact> artifacts)
    {
        string source = artifacts.Count == 0
            ? card.Body.RawText
            : string.Join(Environment.NewLine, artifacts.Select(artifact =>
                $"{artifact.Label}: {artifact.MediaType ?? artifact.Kind}; exists={artifact.Exists.ToString().ToLowerInvariant()}; source={artifact.DeclaredReference ?? "<semantic-only>"}"));
        return new OblivionContentPresentationItem(
            $"{card.Id.Value}.artifact-metadata",
            OblivionContentPresentationKind.ArtifactMetadata,
            OblivionContentPresenterKind.NativeMetadata,
            source,
            Language: null,
            card.Provenance.SourceReference,
            Artifact: null,
            OblivionContentScrollContract.None,
            OblivionContentFocusContract.HostRetainsFocus,
            []);
    }

    private static bool IsPng(OblivionResolvedContentArtifact artifact)
    {
        return string.Equals(artifact.MediaType, "image/png", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(artifact.Kind, "png", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCollapsedSummary(OblivionCard card)
    {
        string[] paragraphs = card.Body.RawText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string summary = paragraphs.FirstOrDefault() ?? card.Subtitle ?? card.Title;
        summary = summary.TrimStart('#', ' ', '\t').Replace("\n", " ", StringComparison.Ordinal);
        return summary.Length <= 180
            ? summary
            : summary[..177] + "...";
    }

    private static string BuildContentTypeLabel(IReadOnlyList<OblivionContentPresentationItem> items)
    {
        return string.Join(" + ", items
            .Select(item => item.ContentKind switch
            {
                OblivionContentPresentationKind.MarkdownDocument => "Markdown",
                OblivionContentPresentationKind.MermaidDiagram => "Mermaid",
                OblivionContentPresentationKind.PngImage => "PNG",
                OblivionContentPresentationKind.Code => item.Language is null ? "Code" : $"Code · {item.Language}",
                OblivionContentPresentationKind.ArtifactMetadata => "Artifact",
                OblivionContentPresentationKind.PlainText => "Text",
                _ => "Unavailable",
            })
            .Distinct(StringComparer.Ordinal));
    }

    private static string? InferLanguage(OblivionCard card)
    {
        string? reference = card.Body.SourceReference ?? card.Provenance.SourceReference;
        return Path.GetExtension(reference)?.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".ts" or ".tsx" => "typescript",
            ".js" or ".jsx" => "javascript",
            ".json" => "json",
            ".toml" => "toml",
            _ => null,
        };
    }
}
