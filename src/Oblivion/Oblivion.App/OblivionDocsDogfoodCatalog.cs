using System.Text.Json;
using Copeland.Markdown;

using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;

namespace Oblivion.App;

public static class OblivionDocsDogfoodCatalog
{
    public const string ProjectionActionId = "oblivion.docs-dogfood.project";
    public const string PageId = "oblivion.docs";
    public const string SectionId = "oblivion";
    public const string WorkspacePageId = "docs";
    public const string IndexCardId = "docs-dogfood-index";

    private static readonly string[] CuratedDocs =
    [
        "docs/Machina.UI/history/machina-oblivion-phase-closeout-m11g.md",
        "docs/Machina.UI/history/machina-oblivion-workspace-persistence-m11d.md",
        "docs/Machina.UI/history/machina-presenter-card-hardening-m11e.md",
        "docs/Machina.UI/history/machina-test-suite-topology-m11b.md",
        "docs/Machina.UI/history/machina-presenter-scrollbar-state-machine-m11c.md",
        "docs/Copeland/history/copeland-markdown-frontend-m12a.md",
        "docs/Machina.UI/history/machina-oblivion-markdown-body-integration-m12b.md",
        "docs/Machina.UI/history/machina-oblivion-markdown-rendering-m12c.md",
        "docs/Aurelian/history/aurelian-monorepo-import-audit-m13a.md",
        "docs/Aurelian/history/aurelian-build-topology-m13b.md",
        "docs/Aurelian/architecture/aurelian-charter.md",
        "docs/Aurelian/architecture/dependency-policy.md",
        "docs/Aurelian/architecture/compositor-policy-mechanism-split.md",
        "docs/Aurelian/architecture/graphics-memory-allocation.md",
        "docs/Aurelian/architecture/mvp-roadmap.md",
        "docs/Aurelian/architecture/world-model-doctrine.md",
    ];

    public static IReadOnlyList<string> GetCuratedDocs()
    {
        return CuratedDocs;
    }

    public static bool IsDocsPage(string sectionId, string pageId)
    {
        return string.Equals(sectionId, SectionId, StringComparison.Ordinal) &&
            string.Equals(pageId, WorkspacePageId, StringComparison.Ordinal);
    }

    public static DocsDogfoodPageData CreatePageData(string workspaceManifestPath)
    {
        ArgumentNullException.ThrowIfNull(workspaceManifestPath);

        string? repoRoot = TryFindRepositoryRoot(Path.GetDirectoryName(workspaceManifestPath));
        List<DocsDogfoodDocumentRecord> docs = [];

        foreach (string relativePath in CuratedDocs)
        {
            docs.Add(CreateDocumentRecord(repoRoot, relativePath));
        }

        DocsDogfoodSummary summary = BuildSummary(docs);
        OblivionCard indexCard = CreateIndexCard(summary);

        List<OblivionCard> cards = [indexCard];
        cards.AddRange(docs.Select(doc => doc.Card));

        return new DocsDogfoodPageData(cards, docs, summary);
    }

    public static (string jsonPath, string textPath) WriteManifest(
        string outputDirectory,
        string workspaceManifestPath)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(workspaceManifestPath);

        Directory.CreateDirectory(outputDirectory);

        DocsDogfoodPageData pageData = CreatePageData(workspaceManifestPath);
        string jsonPath = Path.Combine(outputDirectory, "oblivion-docs-dogfood-manifest.json");
        string textPath = Path.Combine(outputDirectory, "oblivion-docs-dogfood-manifest.txt");

        string[] deferredWork =
        [
            "Markdown editor",
            "File watcher / live editing",
            "Single-file Markdown export/import pipeline",
            "Roslyn compilation and execution",
            "xUnit [Fact] and [Theory] runtime",
            "Visionary code editor/source workspace",
        ];

        var manifest = new
        {
            milestone = "M12d",
            kind = "oblivion-docs-dogfood",
            markdownFrontend = "Copeland.Markdown",
            docsLoaded = pageData.Summary.DocsLoaded,
            cardsGenerated = pageData.Summary.CardsGenerated,
            diagnosticsTotal = pageData.Summary.DiagnosticsTotal,
            unsupportedSyntaxCount = pageData.Summary.UnsupportedSyntaxCount,
            aurelianDocsLoaded = pageData.Summary.AurelianDocsLoaded,
            aurelianDiagnosticsTotal = pageData.Summary.AurelianDiagnosticsTotal,
            docs = pageData.Documents
                .Select(doc => new
                {
                    cardId = doc.Card.Id.Value,
                    sourcePath = doc.SourcePath,
                    diagnosticsCount = doc.Diagnostics.Count,
                    firstHeading = doc.FirstHeading,
                    tags = doc.Card.Tags,
                })
                .ToArray(),
            editorImplemented = false,
            fileWatcherImplemented = false,
            roslynEnabled = false,
            xunitEnabled = false,
            visionaryImplemented = false,
            singleFileMarkdownExportImplemented = false,
            deferredWork,
        };

        string json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });

        string[] textLines =
        [
            "milestone=M12d",
            "kind=oblivion-docs-dogfood",
            "markdownFrontend=Copeland.Markdown",
            $"docsLoaded={pageData.Summary.DocsLoaded}",
            $"cardsGenerated={pageData.Summary.CardsGenerated}",
            $"diagnosticsTotal={pageData.Summary.DiagnosticsTotal}",
            $"unsupportedSyntaxCount={pageData.Summary.UnsupportedSyntaxCount}",
            $"aurelianDocsLoaded={pageData.Summary.AurelianDocsLoaded}",
            $"aurelianDiagnosticsTotal={pageData.Summary.AurelianDiagnosticsTotal}",
            "editorImplemented=false",
            "fileWatcherImplemented=false",
            "roslynEnabled=false",
            "xunitEnabled=false",
            "visionaryImplemented=false",
            "singleFileMarkdownExportImplemented=false",
            $"deferredWork={string.Join(" | ", deferredWork)}",
            "docs:",
            .. pageData.Documents.Select(doc => $"  {doc.Card.Id.Value}|{doc.SourcePath}|diag={doc.Diagnostics.Count}|heading={doc.FirstHeading ?? "<none>"}"),
        ];

        File.WriteAllText(jsonPath, json);
        File.WriteAllLines(textPath, textLines);
        return (jsonPath, textPath);
    }

    private static DocsDogfoodDocumentRecord CreateDocumentRecord(string? repoRoot, string relativePath)
    {
        string normalizedPath = NormalizePath(relativePath);
        string cardId = BuildCardId(normalizedPath);

        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            OblivionWorkspaceDiagnostic diagnostic = OblivionWorkspaceValidator.Error(
                "docs-dogfood-repo-root-missing",
                $"Could not locate the repository root while loading '{normalizedPath}'.",
                normalizedPath);

            return CreateFailedDocumentRecord(cardId, normalizedPath, diagnostic);
        }

        string fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            OblivionWorkspaceDiagnostic diagnostic = OblivionWorkspaceValidator.Error(
                "docs-dogfood-missing-doc",
                $"Markdown dogfood doc '{normalizedPath}' was not found.",
                normalizedPath);

            return CreateFailedDocumentRecord(cardId, normalizedPath, diagnostic);
        }

        string markdownText = File.ReadAllText(fullPath);
        MarkdownCompilation compilation = MarkdownCompiler.Compile(markdownText);
        List<OblivionWorkspaceDiagnostic> diagnostics = CreateMarkdownDiagnostics(compilation, normalizedPath);
        string? firstHeading = TryGetFirstHeading(compilation.Mir);
        string title = string.IsNullOrWhiteSpace(firstHeading)
            ? Path.GetFileNameWithoutExtension(normalizedPath)
            : firstHeading;
        OblivionCardStatus status = diagnostics.Count == 0
            ? OblivionCardStatus.Passing
            : OblivionCardStatus.Warning;

        OblivionCard card = new(
            new OblivionCardId(cardId),
            OblivionCardKind.Note,
            status,
            title,
            Subtitle: normalizedPath,
            Tags: BuildTags(normalizedPath),
            Body: OblivionMarkdownBody.CreateMarkdown(markdownText, normalizedPath),
            Actions: [],
            Artifacts: [],
            Provenance: new OblivionProvenance(
                OblivionProvenanceSourceKind.Generated,
                normalizedPath,
                ProducerActionId: ProjectionActionId));

        return new DocsDogfoodDocumentRecord(card, normalizedPath, firstHeading, diagnostics);
    }

    private static DocsDogfoodDocumentRecord CreateFailedDocumentRecord(
        string cardId,
        string relativePath,
        OblivionWorkspaceDiagnostic diagnostic)
    {
        IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics = [diagnostic];
        string fileName = Path.GetFileNameWithoutExtension(relativePath);
        string markdownText = $"# {fileName}\n\nDoc load failed.\n";
        MarkdownCompilation compilation = MarkdownCompiler.Compile(markdownText);

        OblivionCard card = new(
            new OblivionCardId(cardId),
            OblivionCardKind.Note,
            OblivionCardStatus.Failing,
            fileName,
            Subtitle: relativePath,
            Tags: BuildTags(relativePath),
            Body: OblivionMarkdownBody.CreateMarkdown(markdownText, relativePath),
            Actions: [],
            Artifacts: [],
            Provenance: new OblivionProvenance(
                OblivionProvenanceSourceKind.Generated,
                relativePath,
                ProducerActionId: ProjectionActionId));

        return new DocsDogfoodDocumentRecord(card, relativePath, null, diagnostics);
    }

    private static DocsDogfoodSummary BuildSummary(IReadOnlyList<DocsDogfoodDocumentRecord> documents)
    {
        int docsLoaded = documents.Count(doc => doc.Card.Status != OblivionCardStatus.Failing);
        int diagnosticsTotal = documents.Sum(doc => doc.Diagnostics.Count);
        int unsupportedSyntaxCount = documents.Sum(doc => doc.Diagnostics.Count(IsUnsupportedSyntaxDiagnostic));
        int aurelianDocsLoaded = documents.Count(doc => IsAurelianDoc(doc.SourcePath) && doc.Card.Status != OblivionCardStatus.Failing);
        int aurelianDiagnosticsTotal = documents
            .Where(doc => IsAurelianDoc(doc.SourcePath))
            .Sum(doc => doc.Diagnostics.Count);

        return new DocsDogfoodSummary(
            docsLoaded,
            documents.Count + 1,
            diagnosticsTotal,
            unsupportedSyntaxCount,
            aurelianDocsLoaded,
            aurelianDiagnosticsTotal);
    }

    private static OblivionCard CreateIndexCard(DocsDogfoodSummary summary)
    {
        OblivionCardStatus status = summary.DiagnosticsTotal == 0
            ? OblivionCardStatus.Passing
            : OblivionCardStatus.Warning;

        string bodyText = string.Join(
            '\n',
            [
                $"Docs loaded: {summary.DocsLoaded}",
                $"Aurelian docs loaded: {summary.AurelianDocsLoaded}",
                $"Cards generated: {summary.CardsGenerated}",
                $"Diagnostics total: {summary.DiagnosticsTotal}",
                $"Aurelian diagnostics: {summary.AurelianDiagnosticsTotal}",
                $"Unsupported syntax count: {summary.UnsupportedSyntaxCount}",
                "Docs are edited externally in Notepad or VS Code.",
                "Markdown remains the text-card body language only.",
                "Aurelian docs are dogfood inputs, not runtime or presenter integration behavior.",
                "No editor, file watcher, Roslyn execution, xUnit execution, or Visionary implementation is added here.",
            ]);

        return new OblivionCard(
            new OblivionCardId(IndexCardId),
            OblivionCardKind.Status,
            status,
            "Docs dogfood index",
            "Curated existing repo docs loaded as Markdown cards",
            ["docs", "dogfood", "markdown", "index"],
            OblivionMarkdownBody.CreatePlain(bodyText),
            [],
            [],
            new OblivionProvenance(
                OblivionProvenanceSourceKind.Generated,
                "docs/",
                ProducerActionId: ProjectionActionId));
    }

    private static List<OblivionWorkspaceDiagnostic> CreateMarkdownDiagnostics(
        MarkdownCompilation compilation,
        string sourcePath)
    {
        List<OblivionWorkspaceDiagnostic> diagnostics = [];

        foreach (DocumentDiagnostic diagnostic in compilation.Mir.Diagnostics)
        {
            diagnostics.Add(
                new OblivionWorkspaceDiagnostic(
                    OblivionDiagnosticSeverity.Warning,
                    diagnostic.Id,
                    diagnostic.Message,
                    sourcePath,
                    diagnostic.Severity.ToString(),
                    diagnostic.Span.StartLocation.Line,
                    diagnostic.Span.StartLocation.Column,
                    diagnostic.Span.Start,
                    diagnostic.Span.Length));
        }

        return diagnostics;
    }

    private static bool IsUnsupportedSyntaxDiagnostic(OblivionWorkspaceDiagnostic diagnostic)
    {
        return string.Equals(diagnostic.Code, MarkdownDiagnosticIds.UnsupportedBlockSyntax, StringComparison.Ordinal) ||
            string.Equals(diagnostic.Code, MarkdownDiagnosticIds.UnsupportedInlineSyntax, StringComparison.Ordinal) ||
            string.Equals(diagnostic.Code, MarkdownDiagnosticIds.NestedListNotSupported, StringComparison.Ordinal);
    }

    private static string? TryGetFirstHeading(DocumentMir mir)
    {
        foreach (DocumentBlockMir block in mir.Blocks)
        {
            if (block is HeadingMir heading)
            {
                string headingText = OblivionMarkdownBody.RenderInlineList(heading.Inlines);
                if (!string.IsNullOrWhiteSpace(headingText))
                {
                    return headingText;
                }
            }
        }

        return null;
    }

    private static string BuildCardId(string relativePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(relativePath);
        return $"doc-{fileName}";
    }

    private static string[] BuildTags(string relativePath)
    {
        if (IsAurelianDoc(relativePath))
        {
            return ["aurelian", "docs", "dogfood", "markdown"];
        }

        return ["docs", "dogfood", "markdown"];
    }

    private static bool IsAurelianDoc(string relativePath)
    {
        return relativePath.StartsWith("docs/Aurelian/", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string? TryFindRepositoryRoot(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Copeland.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

public sealed record DocsDogfoodPageData(
    IReadOnlyList<OblivionCard> Cards,
    IReadOnlyList<DocsDogfoodDocumentRecord> Documents,
    DocsDogfoodSummary Summary);

public sealed record DocsDogfoodDocumentRecord(
    OblivionCard Card,
    string SourcePath,
    string? FirstHeading,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics);

public sealed record DocsDogfoodSummary(
    int DocsLoaded,
    int CardsGenerated,
    int DiagnosticsTotal,
    int UnsupportedSyntaxCount,
    int AurelianDocsLoaded,
    int AurelianDiagnosticsTotal);
