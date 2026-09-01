using Oblivion.Model;

namespace Oblivion.Persistence;

public static class OblivionStructuredVaultPaths
{
    public const string WorkspaceManifestFileName = "workspace.json";
    public const string PagesDirectoryName = "pages";
    public const string CardsDirectoryName = "cards";
    public const string ContentDirectoryName = "content";

    public static string WorkspaceManifest(string vaultRoot)
    {
        return Path.Combine(FullRoot(vaultRoot), WorkspaceManifestFileName);
    }

    public static string PageMetadata(string vaultRoot, string pageId)
    {
        ValidateId(pageId, "page");
        return Path.Combine(FullRoot(vaultRoot), PagesDirectoryName, $"{pageId}.toml");
    }

    public static string CardMetadata(string vaultRoot, string cardId)
    {
        ValidateId(cardId, "card");
        return Path.Combine(FullRoot(vaultRoot), CardsDirectoryName, $"{cardId}.toml");
    }

    public static string MarkdownContent(string vaultRoot, string cardId)
    {
        ValidateId(cardId, "card");
        return Path.Combine(FullRoot(vaultRoot), ContentDirectoryName, $"{cardId}.md");
    }

    public static bool IsValidId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or "..")
        {
            return false;
        }

        return id.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');
    }

    private static string FullRoot(string vaultRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRoot);
        return Path.GetFullPath(vaultRoot);
    }

    private static void ValidateId(string id, string kind)
    {
        if (!IsValidId(id))
        {
            throw new ArgumentException(
                $"The {kind} id '{id}' is not a valid structured-vault identity.",
                nameof(id));
        }
    }
}

public static partial class OblivionWorkspaceLoader
{
    public static OblivionWorkspaceLoadResult OpenVault(string vaultRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRoot);

        string fullRoot = Path.GetFullPath(vaultRoot);
        string manifestPath = OblivionStructuredVaultPaths.WorkspaceManifest(fullRoot);
        return LoadStructuredCore(fullRoot, manifestPath);
    }

    private static OblivionWorkspaceLoadResult LoadStructuredCore(
        string workspaceRoot,
        string manifestPath)
    {
        List<OblivionWorkspaceDiagnostic> diagnostics = [];
        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "missing-workspace-manifest",
                $"Structured vault manifest '{manifestPath}' was not found.",
                manifestPath));
            return FailedStructuredLoad(workspaceRoot, manifestPath, diagnostics);
        }

        OblivionWorkspaceJsonReadResult manifestResult = OblivionWorkspaceJsonReader.Read(
            File.ReadAllText(manifestPath),
            manifestPath);
        diagnostics.AddRange(manifestResult.Diagnostics);
        if (manifestResult.Manifest is null)
        {
            return FailedStructuredLoad(workspaceRoot, manifestPath, diagnostics);
        }

        OblivionWorkspaceManifest manifest = manifestResult.Manifest;
        if (manifest.StructuredPageIds.Count == 0)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "missing-page-reference",
                $"Workspace '{manifest.WorkspaceId}' must declare at least one page id in 'pages'.",
                manifestPath));
        }

        if (manifest.Sections.Count != 0)
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "structured-vault-legacy-sections-not-supported",
                $"Workspace '{manifest.WorkspaceId}' uses the structured vault entry and must declare ordered page ids instead of embedded sections.",
                manifestPath));
        }

        List<OblivionWorkspacePage> pages = [];
        HashSet<string> materializedCardIds = new(StringComparer.Ordinal);
        foreach (string pageId in manifest.StructuredPageIds)
        {
            LoadStructuredPage(
                workspaceRoot,
                manifest,
                pageId,
                pages,
                materializedCardIds,
                diagnostics);
        }

        OblivionWorkspace workspace = new(
            new OblivionWorkspaceId(manifest.WorkspaceId),
            manifest.Title,
            string.IsNullOrWhiteSpace(manifest.DefaultPageId)
                ? null
                : new OblivionPageId(manifest.DefaultPageId),
            [new OblivionWorkspaceSection("workspace", manifest.Title, pages)]);

        return new OblivionWorkspaceLoadResult(
            workspace,
            new OblivionWorkspaceLocation(workspaceRoot, manifestPath),
            OblivionWorkspaceValidator.OrderDiagnostics(diagnostics));
    }

    private static void LoadStructuredPage(
        string workspaceRoot,
        OblivionWorkspaceManifest manifest,
        string pageId,
        List<OblivionWorkspacePage> pages,
        HashSet<string> materializedCardIds,
        List<OblivionWorkspaceDiagnostic> diagnostics)
    {
        if (!OblivionStructuredVaultPaths.IsValidId(pageId))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "invalid-page-id",
                $"Workspace '{manifest.WorkspaceId}' page id '{pageId}' cannot map to a canonical metadata path.",
                OblivionStructuredVaultPaths.WorkspaceManifest(workspaceRoot)));
            return;
        }

        string pagePath = OblivionStructuredVaultPaths.PageMetadata(workspaceRoot, pageId);
        if (!File.Exists(pagePath))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "missing-page-metadata",
                $"Workspace '{manifest.WorkspaceId}' page '{pageId}' metadata was not found at its canonical path.",
                pagePath));
            return;
        }

        OblivionPageTomlReadResult pageResult = OblivionPageTomlReader.Read(
            File.ReadAllText(pagePath),
            pagePath);
        AddContext(
            diagnostics,
            pageResult.Diagnostics,
            $"Workspace '{manifest.WorkspaceId}' page '{pageId}': ");
        if (pageResult.Document is null)
        {
            return;
        }

        OblivionPageAssetDocument pageDocument = pageResult.Document;
        if (!string.Equals(pageDocument.Id, pageId, StringComparison.Ordinal))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "page-id-mismatch",
                $"Workspace '{manifest.WorkspaceId}' expected page '{pageId}', but metadata declares '{pageDocument.Id}'.",
                pagePath));
        }

        List<OblivionCard> cards = [];
        HashSet<string> pageCardIds = new(StringComparer.Ordinal);
        foreach (string cardId in pageDocument.StructuredCardIds)
        {
            if (!pageCardIds.Add(cardId))
            {
                diagnostics.Add(OblivionWorkspaceValidator.Error(
                    "duplicate-card-id",
                    $"Workspace '{manifest.WorkspaceId}' page '{pageId}' references card '{cardId}' more than once.",
                    pagePath));
                continue;
            }

            LoadStructuredCard(
                workspaceRoot,
                manifest,
                pageId,
                cardId,
                cards,
                materializedCardIds,
                diagnostics);
        }

        pages.Add(new OblivionWorkspacePage(
            new OblivionPageId(pageId),
            pageDocument.Title,
            pageDocument.Description,
            pageDocument.Tags,
            cards));
    }

    private static void LoadStructuredCard(
        string workspaceRoot,
        OblivionWorkspaceManifest manifest,
        string pageId,
        string cardId,
        List<OblivionCard> cards,
        HashSet<string> materializedCardIds,
        List<OblivionWorkspaceDiagnostic> diagnostics)
    {
        if (!OblivionStructuredVaultPaths.IsValidId(cardId))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "invalid-card-id",
                $"Workspace '{manifest.WorkspaceId}' page '{pageId}' card id '{cardId}' cannot map to a canonical metadata path.",
                OblivionStructuredVaultPaths.PageMetadata(workspaceRoot, pageId)));
            return;
        }

        string cardPath = OblivionStructuredVaultPaths.CardMetadata(workspaceRoot, cardId);
        if (!File.Exists(cardPath))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "missing-card-metadata",
                $"Workspace '{manifest.WorkspaceId}' page '{pageId}' card '{cardId}' metadata was not found at its canonical path.",
                cardPath));
            return;
        }

        OblivionCardTomlReadResult cardResult = OblivionCardTomlReader.Read(
            File.ReadAllText(cardPath),
            cardPath);
        AddContext(
            diagnostics,
            cardResult.Diagnostics,
            $"Workspace '{manifest.WorkspaceId}' page '{pageId}' card '{cardId}': ");
        if (cardResult.Document is null)
        {
            return;
        }

        OblivionCardAssetDocument cardDocument = cardResult.Document;
        if (!string.Equals(cardDocument.Id, cardId, StringComparison.Ordinal))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "card-id-mismatch",
                $"Workspace '{manifest.WorkspaceId}' page '{pageId}' expected card '{cardId}', but metadata declares '{cardDocument.Id}'.",
                cardPath));
            return;
        }

        if (!materializedCardIds.Add(cardDocument.Id))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "duplicate-card-id",
                $"Workspace '{manifest.WorkspaceId}' materializes card id '{cardDocument.Id}' more than once.",
                cardPath));
            return;
        }

        bool isDiagramCard = string.Equals(cardDocument.CardKind, "diagram", StringComparison.Ordinal);
        bool isTableCard = string.Equals(cardDocument.CardKind, "table", StringComparison.Ordinal);
        bool isFunctionCard = string.Equals(cardDocument.CardKind, "function", StringComparison.Ordinal);
        if (!isDiagramCard &&
            !isTableCard &&
            !isFunctionCard &&
            (!string.Equals(cardDocument.Body.Format, "copeland-markdown", StringComparison.Ordinal) ||
             string.IsNullOrWhiteSpace(cardDocument.Body.Path)))
        {
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "structured-card-markdown-reference-required",
                $"Workspace '{manifest.WorkspaceId}' page '{pageId}' card '{cardId}' must declare one vault-relative Markdown body path.",
                cardPath));
            return;
        }

        int diagnosticCountBeforeBodyLoad = diagnostics.Count;
        OblivionCard card = BuildCard(
            cardDocument,
            workspaceRoot,
            diagnostics,
            cardPath,
            new OblivionWorkspaceLoadOptions(),
            new OblivionPageId(pageId),
            new OblivionWorkspaceId(manifest.WorkspaceId));
        AddContextToRange(
            diagnostics,
            diagnosticCountBeforeBodyLoad,
            $"Workspace '{manifest.WorkspaceId}' page '{pageId}' card '{cardId}': ");
        cards.Add(card);
    }

    private static OblivionWorkspaceLoadResult FailedStructuredLoad(
        string workspaceRoot,
        string manifestPath,
        IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        return new OblivionWorkspaceLoadResult(
            null,
            new OblivionWorkspaceLocation(workspaceRoot, manifestPath),
            OblivionWorkspaceValidator.OrderDiagnostics(diagnostics));
    }

    private static void AddContext(
        List<OblivionWorkspaceDiagnostic> destination,
        IReadOnlyList<OblivionWorkspaceDiagnostic> source,
        string context)
    {
        destination.AddRange(source.Select(diagnostic => diagnostic with
        {
            Message = context + diagnostic.Message,
        }));
    }

    private static void AddContextToRange(
        List<OblivionWorkspaceDiagnostic> diagnostics,
        int startIndex,
        string context)
    {
        for (int index = startIndex; index < diagnostics.Count; index++)
        {
            diagnostics[index] = diagnostics[index] with
            {
                Message = context + diagnostics[index].Message,
            };
        }
    }
}
