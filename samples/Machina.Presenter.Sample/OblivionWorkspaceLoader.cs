using System.Collections.Concurrent;
using Copeland.Markdown;

namespace Machina.Presenter.Sample;

public static class OblivionWorkspaceLoader
{
    private static readonly ConcurrentDictionary<string, OblivionWorkspaceLoadResult> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static OblivionWorkspaceLoadResult Load(
        string manifestPath,
        OblivionWorkspaceLoadOptions? options = null,
        bool useCache = true)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);

        string fullManifestPath = Path.GetFullPath(manifestPath);
        if (useCache)
        {
            return Cache.GetOrAdd(
                BuildCacheKey(fullManifestPath, options),
                _ => LoadCore(fullManifestPath, options ?? new OblivionWorkspaceLoadOptions()));
        }

        return LoadCore(fullManifestPath, options ?? new OblivionWorkspaceLoadOptions());
    }

    public static void ClearCache()
    {
        Cache.Clear();
    }

    private static OblivionWorkspaceLoadResult LoadCore(
        string manifestPath,
        OblivionWorkspaceLoadOptions options)
    {
        List<OblivionWorkspaceDiagnostic> diagnostics = [];

        if (!File.Exists(manifestPath))
        {
            return new OblivionWorkspaceLoadResult(
                null,
                [OblivionWorkspaceValidator.Error("missing-workspace-manifest", $"Workspace manifest '{manifestPath}' was not found.", manifestPath)]);
        }

        string workspaceRoot = Path.GetDirectoryName(manifestPath) ?? Path.GetFullPath(".");
        string json = File.ReadAllText(manifestPath);
        OblivionWorkspaceJsonReadResult manifestResult = OblivionWorkspaceJsonReader.Read(json, manifestPath);
        diagnostics.AddRange(manifestResult.Diagnostics);

        if (manifestResult.Manifest is null)
        {
            return new OblivionWorkspaceLoadResult(null, OblivionWorkspaceValidator.OrderDiagnostics(diagnostics));
        }

        List<OblivionWorkspaceSection> sections = [];
        foreach (OblivionWorkspaceSectionManifest sectionManifest in manifestResult.Manifest.Sections)
        {
            List<OblivionWorkspacePage> pages = [];

            foreach (OblivionWorkspacePageManifest pageManifest in sectionManifest.Pages)
            {
                string presenterPageId = $"{sectionManifest.Id}.{pageManifest.Id}";
                string pageTitle = pageManifest.Title;
                string? pageDescription = null;
                IReadOnlyList<string> pageTags = [];
                string? pageAssetPath = null;

                if (!string.IsNullOrWhiteSpace(pageManifest.Asset))
                {
                    string? resolvedPageAssetPath = ResolveAssetPath(workspaceRoot, pageManifest.Asset!, options, diagnostics, manifestPath, "page asset");
                    if (resolvedPageAssetPath is not null)
                    {
                        pageAssetPath = GetRelativePath(workspaceRoot, resolvedPageAssetPath);
                        if (File.Exists(resolvedPageAssetPath))
                        {
                            OblivionPageTomlReadResult pageResult = OblivionPageTomlReader.Read(File.ReadAllText(resolvedPageAssetPath), resolvedPageAssetPath);
                            diagnostics.AddRange(pageResult.Diagnostics);

                            if (pageResult.Document is not null)
                            {
                                if (!string.Equals(pageResult.Document.Id, pageManifest.Id, StringComparison.Ordinal))
                                {
                                    diagnostics.Add(OblivionWorkspaceValidator.Error("page-id-mismatch", $"Page asset id '{pageResult.Document.Id}' does not match manifest page id '{pageManifest.Id}'.", resolvedPageAssetPath));
                                }

                                pageTitle = pageResult.Document.Title;
                                pageDescription = pageResult.Document.Description;
                                pageTags = pageResult.Document.Tags;
                            }
                        }
                        else
                        {
                            diagnostics.Add(OblivionWorkspaceValidator.Error("missing-page-asset", $"Page asset '{pageManifest.Asset}' was not found.", resolvedPageAssetPath));
                        }
                    }
                }

                List<OblivionCard> cards = [];
                foreach (string cardAssetReference in pageManifest.Cards)
                {
                    string? resolvedCardPath = ResolveAssetPath(workspaceRoot, cardAssetReference, options, diagnostics, manifestPath, "card asset");
                    if (resolvedCardPath is null)
                    {
                        continue;
                    }

                    if (!File.Exists(resolvedCardPath))
                    {
                        diagnostics.Add(OblivionWorkspaceValidator.Error("missing-card-asset", $"Card asset '{cardAssetReference}' was not found.", resolvedCardPath));
                        continue;
                    }

                    OblivionCardTomlReadResult cardResult = OblivionCardTomlReader.Read(File.ReadAllText(resolvedCardPath), resolvedCardPath);
                    diagnostics.AddRange(cardResult.Diagnostics);
                    if (cardResult.Document is null)
                    {
                        continue;
                    }

                    cards.Add(BuildCard(cardResult.Document, workspaceRoot, diagnostics, resolvedCardPath, options));
                }

                pages.Add(
                    new OblivionWorkspacePage(
                        sectionManifest.Id,
                        pageManifest.Id,
                        presenterPageId,
                        pageTitle,
                        pageDescription,
                        pageTags,
                        pageAssetPath,
                        cards));
            }

            sections.Add(new OblivionWorkspaceSection(sectionManifest.Id, sectionManifest.Title, pages));
        }

        OblivionWorkspace workspace = new(
            workspaceRoot,
            manifestPath,
            manifestResult.Manifest.WorkspaceId,
            manifestResult.Manifest.Title,
            manifestResult.Manifest.DefaultPageId,
            sections);

        return new OblivionWorkspaceLoadResult(workspace, OblivionWorkspaceValidator.OrderDiagnostics(diagnostics));
    }

    private static OblivionCard BuildCard(
        OblivionCardAssetDocument document,
        string workspaceRoot,
        List<OblivionWorkspaceDiagnostic> diagnostics,
        string sourcePath,
        OblivionWorkspaceLoadOptions options)
    {
        _ = OblivionWorkspaceValidator.TryParseCardKind(document.CardKind, out OblivionCardKind cardKind);
        _ = OblivionWorkspaceValidator.TryParseCardStatus(document.Status, out OblivionCardStatus status);

        List<OblivionCardArtifact> artifacts = [];
        foreach (OblivionCardArtifactDocument artifactDocument in document.Artifacts)
        {
            artifacts.Add(BuildArtifact(artifactDocument, workspaceRoot, diagnostics, sourcePath, options));
        }

        OblivionCardBody body = BuildBody(document.Body, workspaceRoot, diagnostics, sourcePath, options);

        return new OblivionCard(
            new OblivionCardId(document.Id),
            cardKind,
            status,
            document.Title,
            document.Subtitle,
            document.Tags,
            body,
            document.Actions.Select(action => new OblivionCardAction(action.Id, action.Label, action.Enabled)).ToArray(),
            artifacts,
            SourcePath: GetRelativePath(workspaceRoot, sourcePath));
    }

    private static OblivionCardBody BuildBody(
        OblivionCardBodyDocument bodyDocument,
        string workspaceRoot,
        List<OblivionWorkspaceDiagnostic> diagnostics,
        string sourcePath,
        OblivionWorkspaceLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(bodyDocument);

        if (string.Equals(bodyDocument.Format, "plain", StringComparison.Ordinal))
        {
            return OblivionMarkdownBody.CreatePlain(bodyDocument.Text ?? string.Empty);
        }

        if (!string.Equals(bodyDocument.Format, "copeland-markdown", StringComparison.Ordinal))
        {
            return new OblivionCardBody(
                OblivionCardBodyFormat.Plain,
                RawText: bodyDocument.Text,
                BodySourcePath: null,
                PreviewLines: ["Unsupported card body format."],
                DocumentMir: null,
                Diagnostics:
                [
                    OblivionWorkspaceValidator.Error("unknown-body-format", $"Body format '{bodyDocument.Format}' is not supported.", sourcePath),
                ]);
        }

        string markdownText;
        string? markdownSourcePath = null;
        List<OblivionWorkspaceDiagnostic> markdownDiagnostics = [];

        if (!string.IsNullOrWhiteSpace(bodyDocument.Path))
        {
            string? resolvedBodyPath = ResolveAssetPath(workspaceRoot, bodyDocument.Path!, options, diagnostics, sourcePath, "markdown body");
            if (resolvedBodyPath is null)
            {
                markdownDiagnostics.Add(OblivionWorkspaceValidator.Error("invalid-markdown-body-path", $"Markdown body path '{bodyDocument.Path}' could not be resolved.", sourcePath));
                return OblivionMarkdownBody.CreateMarkdown(
                    string.Empty,
                    bodyDocument.Path,
                    MarkdownCompiler.Compile(string.Empty),
                    markdownDiagnostics);
            }

            markdownSourcePath = GetRelativePath(workspaceRoot, resolvedBodyPath);
            if (!File.Exists(resolvedBodyPath))
            {
                OblivionWorkspaceDiagnostic missingDiagnostic = OblivionWorkspaceValidator.Error(
                    "missing-markdown-body-file",
                    $"Markdown body file '{bodyDocument.Path}' was not found.",
                    resolvedBodyPath);
                diagnostics.Add(missingDiagnostic);
                markdownDiagnostics.Add(missingDiagnostic);

                return OblivionMarkdownBody.CreateMarkdown(
                    string.Empty,
                    markdownSourcePath,
                    MarkdownCompiler.Compile(string.Empty),
                    markdownDiagnostics);
            }

            markdownText = File.ReadAllText(resolvedBodyPath);
        }
        else
        {
            markdownText = bodyDocument.Text ?? string.Empty;
        }

        MarkdownCompilation compilation = MarkdownCompiler.Compile(markdownText);
        foreach (DocumentDiagnostic diagnostic in compilation.Mir.Diagnostics)
        {
            markdownDiagnostics.Add(
                new OblivionWorkspaceDiagnostic(
                    OblivionWorkspaceDiagnosticSeverity.Warning,
                    diagnostic.Id,
                    diagnostic.Message,
                    markdownSourcePath ?? sourcePath,
                    diagnostic.Severity.ToString(),
                    diagnostic.Span.StartLocation.Line,
                    diagnostic.Span.StartLocation.Column,
                    diagnostic.Span.Start,
                    diagnostic.Span.Length));
        }

        diagnostics.AddRange(markdownDiagnostics);
        return OblivionMarkdownBody.CreateMarkdown(markdownText, markdownSourcePath, compilation, markdownDiagnostics);
    }

    private static OblivionCardArtifact BuildArtifact(
        OblivionCardArtifactDocument artifactDocument,
        string workspaceRoot,
        List<OblivionWorkspaceDiagnostic> diagnostics,
        string sourcePath,
        OblivionWorkspaceLoadOptions options)
    {
        string id = artifactDocument.Id;
        string label = artifactDocument.Label;
        string kind = artifactDocument.Kind;
        string? path = artifactDocument.Path;
        string? artifactSourcePath = null;

        if (!string.IsNullOrWhiteSpace(artifactDocument.Asset))
        {
            string? resolvedArtifactPath = ResolveAssetPath(workspaceRoot, artifactDocument.Asset!, options, diagnostics, sourcePath, "artifact asset");
            if (resolvedArtifactPath is not null)
            {
                artifactSourcePath = GetRelativePath(workspaceRoot, resolvedArtifactPath);
                if (File.Exists(resolvedArtifactPath))
                {
                    OblivionArtifactTomlReadResult artifactResult = OblivionArtifactTomlReader.Read(File.ReadAllText(resolvedArtifactPath), resolvedArtifactPath);
                    diagnostics.AddRange(artifactResult.Diagnostics);

                    if (artifactResult.Document is not null)
                    {
                        if (!string.Equals(artifactResult.Document.Id, artifactDocument.Id, StringComparison.Ordinal))
                        {
                            diagnostics.Add(OblivionWorkspaceValidator.Error("artifact-id-mismatch", $"Artifact asset id '{artifactResult.Document.Id}' does not match card artifact id '{artifactDocument.Id}'.", resolvedArtifactPath));
                        }

                        label = artifactResult.Document.Label;
                        kind = artifactResult.Document.ArtifactKind;
                        path = artifactResult.Document.Path;
                    }
                }
                else
                {
                    diagnostics.Add(OblivionWorkspaceValidator.Error("missing-artifact-asset", $"Artifact asset '{artifactDocument.Asset}' was not found.", resolvedArtifactPath));
                }
            }
        }

        return new OblivionCardArtifact(
            id,
            label,
            kind,
            path,
            artifactDocument.Generated,
            SourcePath: artifactSourcePath);
    }

    private static string? ResolveAssetPath(
        string workspaceRoot,
        string assetPath,
        OblivionWorkspaceLoadOptions options,
        List<OblivionWorkspaceDiagnostic> diagnostics,
        string? sourcePath,
        string assetKind)
    {
        if (Path.IsPathRooted(assetPath))
        {
            if (!options.AllowAbsolutePaths)
            {
                diagnostics.Add(OblivionWorkspaceValidator.Error("absolute-path-not-allowed", $"The {assetKind} path '{assetPath}' must be relative to the workspace root.", sourcePath));
                return null;
            }

            return Path.GetFullPath(assetPath);
        }

        string fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, assetPath));
        string fullRoot = EnsureTrailingSeparator(Path.GetFullPath(workspaceRoot));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error("path-traversal-not-allowed", $"The {assetKind} path '{assetPath}' escapes the workspace root.", sourcePath));
            return null;
        }

        return fullPath;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string GetRelativePath(string root, string fullPath)
    {
        return Path.GetRelativePath(root, fullPath).Replace('\\', '/');
    }

    private static string BuildCacheKey(string manifestPath, OblivionWorkspaceLoadOptions? options)
    {
        bool allowAbsolutePaths = options?.AllowAbsolutePaths ?? false;
        return $"{manifestPath}|allowAbsolutePaths={allowAbsolutePaths}";
    }
}

public static class OblivionWorkspacePaths
{
    public static string ResolveWorkspaceManifestPath(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        foreach (string candidate in GetDefaultWorkspaceCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "OblivionSampleWorkspace", "workspace.oblivion.json"));
    }

    public static bool HasDefaultWorkspace()
    {
        return GetDefaultWorkspaceCandidates().Any(File.Exists);
    }

    private static IEnumerable<string> GetDefaultWorkspaceCandidates()
    {
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "OblivionSampleWorkspace", "workspace.oblivion.json"));

        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        yield return Path.Combine(repoRoot, "samples", "Machina.Presenter.Sample", "OblivionSampleWorkspace", "workspace.oblivion.json");

        yield return Path.Combine(Directory.GetCurrentDirectory(), "samples", "Machina.Presenter.Sample", "OblivionSampleWorkspace", "workspace.oblivion.json");
    }
}
