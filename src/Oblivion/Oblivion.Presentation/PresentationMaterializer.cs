using Oblivion.Model;

namespace Oblivion.Presentation;

public enum PresentationDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record PresentationDiagnostic(
    string Code,
    PresentationDiagnosticSeverity Severity,
    string Message,
    string? ContentId = null,
    string? LayoutGroupId = null);

public enum PresentationMaterializedBandKind
{
    Stream,
    Compare,
    Columns,
    Focus,
}

public sealed record PresentationMaterializedBand(
    string Id,
    PresentationMaterializedBandKind Kind,
    IReadOnlyList<PresentationContentId> ContentIds,
    IReadOnlyList<OblivionCardId> CardIds);

public sealed record PresentationMaterializedContent(
    PresentationContentId ContentId,
    string ContentKind,
    OblivionCardId CardId,
    string? SourceReference,
    string? LayoutGroupId,
    OblivionProvenance Provenance);

public sealed record MaterializedPresentation(
    Presentation Source,
    OblivionWorkspace Workspace,
    OblivionWorkspacePage Page,
    IReadOnlyList<PresentationMaterializedContent> Content,
    IReadOnlyList<PresentationMaterializedBand> Bands,
    IReadOnlyList<PresentationDiagnostic> Diagnostics);

public sealed class PresentationValidationException : Exception
{
    public PresentationValidationException(IReadOnlyList<PresentationDiagnostic> diagnostics)
        : base(string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message)))
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<PresentationDiagnostic> Diagnostics { get; }
}

public static class PresentationMaterializer
{
    private const string Producer = "oblivion.presentation.materializer.v1";

    public static MaterializedPresentation Materialize(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        IReadOnlyList<PresentationDiagnostic> diagnostics = Validate(presentation);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == PresentationDiagnosticSeverity.Error))
        {
            throw new PresentationValidationException(diagnostics);
        }

        OblivionWorkspaceId workspaceId = new($"presentation.{presentation.Id.Value}");
        OblivionPageId pageId = new("cards");
        Dictionary<PresentationContentId, string> layoutMembership = presentation.Layout
            .SelectMany(group => group.ContentIds.Select(contentId => (contentId, group.Id)))
            .ToDictionary(item => item.contentId, item => item.Id);

        List<OblivionCard> cards = [];
        List<PresentationMaterializedContent> materializedContent = [];
        foreach (PresentationContent content in presentation.Content)
        {
            OblivionCardId cardId = CreateCardId(presentation.Id, content.Id);
            OblivionProvenance provenance = CreateProvenance(presentation.Id, content);
            OblivionCard card = CreateCard(content, cardId, pageId, workspaceId, provenance);
            cards.Add(card);
            materializedContent.Add(new PresentationMaterializedContent(
                content.Id,
                ContentKind(content),
                cardId,
                SourceReference(content),
                layoutMembership.GetValueOrDefault(content.Id),
                provenance));
        }

        OblivionWorkspacePage page = new(
            pageId,
            presentation.Title,
            $"Materialized from semantic presentation '{presentation.Id.Value}'.",
            ["presentation", presentation.Id.Value],
            cards);
        OblivionWorkspace workspace = new(
            workspaceId,
            presentation.Title,
            pageId,
            [new OblivionWorkspaceSection("oblivion", "Presentation", [page])]);

        return new MaterializedPresentation(
            presentation,
            workspace,
            page,
            materializedContent,
            CreateBands(presentation, materializedContent),
            diagnostics);
    }

    public static IReadOnlyList<PresentationDiagnostic> Validate(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        List<PresentationDiagnostic> diagnostics = [];
        HashSet<PresentationContentId> contentIds = [];

        foreach (PresentationContent content in presentation.Content)
        {
            if (!contentIds.Add(content.Id))
            {
                diagnostics.Add(Error(
                    "OBLIVION-PRESENTATION-DUPLICATE-CONTENT-ID",
                    $"Presentation '{presentation.Id.Value}' contains duplicate content ID '{content.Id.Value}'.",
                    contentId: content.Id.Value));
            }

            ValidateContent(content, diagnostics);
        }

        HashSet<string> layoutIds = new(StringComparer.Ordinal);
        Dictionary<PresentationContentId, string> membership = [];
        foreach (PresentationLayoutGroup group in presentation.Layout)
        {
            if (!layoutIds.Add(group.Id))
            {
                diagnostics.Add(Error(
                    "OBLIVION-PRESENTATION-DUPLICATE-LAYOUT-ID",
                    $"Layout group ID '{group.Id}' is duplicated.",
                    layoutGroupId: group.Id));
            }

            ValidateGroupShape(group, diagnostics);
            HashSet<PresentationContentId> withinGroup = [];
            foreach (PresentationContentId contentId in group.ContentIds)
            {
                if (!withinGroup.Add(contentId))
                {
                    diagnostics.Add(Error(
                        "OBLIVION-PRESENTATION-DUPLICATE-GROUP-MEMBER",
                        $"Layout group '{group.Id}' references content '{contentId.Value}' more than once.",
                        contentId.Value,
                        group.Id));
                }

                if (!contentIds.Contains(contentId))
                {
                    diagnostics.Add(Error(
                        "OBLIVION-PRESENTATION-UNKNOWN-CONTENT-ID",
                        $"Layout group '{group.Id}' references unknown content ID '{contentId.Value}'.",
                        contentId.Value,
                        group.Id));
                }

                if (membership.TryGetValue(contentId, out string? existingGroupId))
                {
                    diagnostics.Add(Error(
                        "OBLIVION-PRESENTATION-MULTIPLE-LAYOUT-MEMBERSHIP",
                        $"Content '{contentId.Value}' belongs to incompatible layout groups '{existingGroupId}' and '{group.Id}'.",
                        contentId.Value,
                        group.Id));
                }
                else
                {
                    membership[contentId] = group.Id;
                }
            }
        }

        return diagnostics;
    }

    public static OblivionCardId CreateCardId(PresentationId presentationId, PresentationContentId contentId)
    {
        return new OblivionCardId($"presentation.{presentationId.Value}.{contentId.Value}");
    }

    private static void ValidateContent(
        PresentationContent content,
        List<PresentationDiagnostic> diagnostics)
    {
        if (content is NextActionsContent nextActions && nextActions.Items.Count == 0)
        {
            diagnostics.Add(Error(
                "OBLIVION-PRESENTATION-EMPTY-NEXT-ACTIONS",
                $"Next actions content '{content.Id.Value}' must contain at least one item.",
                content.Id.Value));
        }

        if (content is CodeContent code &&
            ((code.StartLine is null) != (code.EndLine is null) ||
             code.StartLine is <= 0 ||
             code.EndLine < code.StartLine))
        {
            diagnostics.Add(Error(
                "OBLIVION-PRESENTATION-INVALID-CODE-RANGE",
                $"Code content '{content.Id.Value}' has an invalid line range.",
                content.Id.Value));
        }
    }

    private static void ValidateGroupShape(
        PresentationLayoutGroup group,
        List<PresentationDiagnostic> diagnostics)
    {
        int count = group.ContentIds.Count;
        bool valid = group switch
        {
            CompareLayoutGroup => count == 2,
            ColumnsLayoutGroup => count is >= 2 and <= 3,
            FocusLayoutGroup => count == 1,
            _ => false,
        };

        if (!valid)
        {
            diagnostics.Add(Error(
                "OBLIVION-PRESENTATION-INVALID-LAYOUT-GROUP",
                $"Layout group '{group.Id}' has {count} members; Compare requires 2, Columns supports 2 or 3, and Focus requires 1.",
                layoutGroupId: group.Id));
        }
    }

    private static OblivionCard CreateCard(
        PresentationContent content,
        OblivionCardId cardId,
        OblivionPageId pageId,
        OblivionWorkspaceId workspaceId,
        OblivionProvenance provenance)
    {
        return content switch
        {
            SummaryContent summary => Card(
                content,
                cardId,
                OblivionCardKind.Status,
                summary.Title ?? "Summary",
                new OblivionCardBody(
                    OblivionCardBodyFormat.Plain,
                    new OblivionPlainTextContent(summary.Text)),
                [],
                pageId,
                workspaceId,
                provenance),
            MarkdownContent markdown => Card(
                content,
                cardId,
                OblivionCardKind.Note,
                markdown.Title ?? "Document",
                MarkdownBody(markdown.Source),
                [],
                pageId,
                workspaceId,
                provenance),
            CodeContent code => Card(
                content,
                cardId,
                OblivionCardKind.CodeFact,
                code.Title ?? "Code",
                CodeBody(code),
                [],
                pageId,
                workspaceId,
                provenance),
            DiagramContent diagram => Card(
                content,
                cardId,
                OblivionCardKind.Note,
                diagram.Title ?? "Diagram",
                DiagramBody(diagram.Source),
                [],
                pageId,
                workspaceId,
                provenance),
            ArtifactContent artifact => Card(
                content,
                cardId,
                OblivionCardKind.Artifact,
                artifact.Title ?? artifact.Label ?? "Artifact",
                new OblivionCardBody(
                    OblivionCardBodyFormat.Plain,
                    new OblivionPlainTextContent(
                        $"Resolved {artifact.Kind} artifact: {artifact.Label ?? artifact.Title ?? content.Id.Value}")),
                [new OblivionCardArtifact(
                    $"{content.Id.Value}.artifact",
                    artifact.Label ?? artifact.Title ?? content.Id.Value,
                    artifact.Kind,
                    artifact.Reference,
                    artifact.Generated,
                    artifact.Reference)],
                pageId,
                workspaceId,
                provenance),
            DecisionContent decision => Card(
                content,
                cardId,
                OblivionCardKind.Note,
                decision.Title ?? "Decision",
                new OblivionCardBody(
                    OblivionCardBodyFormat.Plain,
                    new OblivionPlainTextContent(DecisionText(decision))),
                [],
                pageId,
                workspaceId,
                provenance),
            NextActionsContent nextActions => Card(
                content,
                cardId,
                OblivionCardKind.Note,
                nextActions.Title ?? "Next actions",
                new OblivionCardBody(
                    OblivionCardBodyFormat.Plain,
                    new OblivionPlainTextContent(string.Join(
                        Environment.NewLine,
                        nextActions.Items.Select((item, index) => $"{index + 1}. {item}")))),
                [],
                pageId,
                workspaceId,
                provenance),
            _ => throw new InvalidOperationException($"Unsupported presentation content type '{content.GetType().Name}'."),
        };
    }

    private static OblivionCard Card(
        PresentationContent content,
        OblivionCardId cardId,
        OblivionCardKind kind,
        string title,
        OblivionCardBody body,
        IReadOnlyList<OblivionCardArtifact> artifacts,
        OblivionPageId pageId,
        OblivionWorkspaceId workspaceId,
        OblivionProvenance provenance)
    {
        bool compactArtifact = content is ArtifactContent;
        return new OblivionCard(
            cardId,
            kind,
            OblivionCardStatus.Passing,
            title,
            compactArtifact ? null : Subtitle(content),
            compactArtifact ? [] : ["presentation", ContentKind(content).ToLowerInvariant()],
            body,
            [],
            artifacts,
            provenance,
            pageId,
            workspaceId);
    }

    private static OblivionCardBody MarkdownBody(PresentationSource source)
    {
        OblivionCardContent content = source.Reference is null
            ? new OblivionInlineMarkdownContent(source.Content)
            : new OblivionMarkdownReferenceContent(source.Content, source.Reference);
        return new OblivionCardBody(OblivionCardBodyFormat.CopelandMarkdown, content);
    }

    private static OblivionCardBody CodeBody(CodeContent code)
    {
        string text = code.StartLine is null
            ? code.Source.Content
            : SelectLines(code.Source.Content, code.StartLine.Value, code.EndLine!.Value);
        OblivionCardContent content = code.Source.Reference is null
            ? new OblivionPlainTextContent(text)
            : new OblivionMarkdownReferenceContent(text, code.Source.Reference);
        return new OblivionCardBody(OblivionCardBodyFormat.Plain, content);
    }

    private static OblivionCardBody DiagramBody(DiagramSource source)
    {
        if (source is not DiagramSource.Mermaid mermaid)
        {
            throw new InvalidOperationException($"Unsupported diagram source type '{source.GetType().Name}'.");
        }

        string markdown = $"```mermaid{Environment.NewLine}{mermaid.Source.Content}{Environment.NewLine}```";
        return MarkdownBody(new PresentationSource(markdown, mermaid.Source.Reference));
    }

    private static string SelectLines(string source, int startLine, int endLine)
    {
        string[] lines = source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        int startIndex = Math.Min(startLine - 1, lines.Length);
        int count = Math.Min(endLine - startLine + 1, lines.Length - startIndex);
        return string.Join(Environment.NewLine, lines.Skip(startIndex).Take(count));
    }

    private static string DecisionText(DecisionContent decision)
    {
        if (decision.Evidence.Count == 0)
        {
            return decision.Text;
        }

        return $"{decision.Text}{Environment.NewLine}{Environment.NewLine}Evidence: {string.Join(", ", decision.Evidence)}";
    }

    private static OblivionProvenance CreateProvenance(
        PresentationId presentationId,
        PresentationContent content)
    {
        if (content.Provenance is not null)
        {
            return content.Provenance;
        }

        return new OblivionProvenance(
            OblivionProvenanceSourceKind.Generated,
            SourceReference(content) ?? $"presentation:{presentationId.Value}/content:{content.Id.Value}",
            $"{Producer};presentation={presentationId.Value};content={content.Id.Value}");
    }

    private static string? SourceReference(PresentationContent content)
    {
        return content switch
        {
            MarkdownContent markdown => markdown.Source.Reference,
            CodeContent code => code.Source.Reference,
            DiagramContent { Source: DiagramSource.Mermaid mermaid } => mermaid.Source.Reference,
            ArtifactContent artifact => artifact.Reference,
            _ => null,
        };
    }

    private static string Subtitle(PresentationContent content)
    {
        return $"Presentation content · {ContentKind(content)} · {content.Id.Value}";
    }

    private static string ContentKind(PresentationContent content)
    {
        return content switch
        {
            SummaryContent => "Summary",
            MarkdownContent => "Markdown",
            CodeContent => "Code",
            DiagramContent => "Diagram",
            ArtifactContent => "Artifact",
            DecisionContent => "Decision",
            NextActionsContent => "NextActions",
            _ => content.GetType().Name,
        };
    }

    private static IReadOnlyList<PresentationMaterializedBand> CreateBands(
        Presentation presentation,
        IReadOnlyList<PresentationMaterializedContent> content)
    {
        Dictionary<PresentationContentId, PresentationLayoutGroup> groupsByContent = presentation.Layout
            .SelectMany(group => group.ContentIds.Select(contentId => (contentId, group)))
            .ToDictionary(item => item.contentId, item => item.group);
        HashSet<string> emittedGroups = new(StringComparer.Ordinal);
        List<PresentationMaterializedBand> bands = [];

        foreach (PresentationMaterializedContent item in content)
        {
            if (!groupsByContent.TryGetValue(item.ContentId, out PresentationLayoutGroup? group))
            {
                bands.Add(new PresentationMaterializedBand(
                    $"stream.{item.ContentId.Value}",
                    PresentationMaterializedBandKind.Stream,
                    [item.ContentId],
                    [item.CardId]));
                continue;
            }

            if (!emittedGroups.Add(group.Id))
            {
                continue;
            }

            bands.Add(new PresentationMaterializedBand(
                group.Id,
                group switch
                {
                    CompareLayoutGroup => PresentationMaterializedBandKind.Compare,
                    ColumnsLayoutGroup => PresentationMaterializedBandKind.Columns,
                    FocusLayoutGroup => PresentationMaterializedBandKind.Focus,
                    _ => throw new InvalidOperationException($"Unsupported layout group '{group.GetType().Name}'."),
                },
                group.ContentIds,
                group.ContentIds.Select(contentId =>
                    content.Single(item => item.ContentId == contentId).CardId).ToArray()));
        }

        return bands;
    }

    private static PresentationDiagnostic Error(
        string code,
        string message,
        string? contentId = null,
        string? layoutGroupId = null)
    {
        return new PresentationDiagnostic(
            code,
            PresentationDiagnosticSeverity.Error,
            message,
            contentId,
            layoutGroupId);
    }
}
