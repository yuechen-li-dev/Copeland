using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionControlDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? WorkspaceId,
    string? PageId,
    string? CardId,
    string? Source,
    int? Line,
    int? Column);

public sealed record OblivionWorkspaceInfo(
    string WorkspaceId,
    string Title,
    int FormatVersion,
    string? DefaultPageId,
    int PageCount,
    int CardCount,
    string WorkspaceRoot);

public sealed record OblivionWorkspaceValidation(
    bool Valid,
    int ErrorCount,
    int WarningCount,
    int PageCount,
    int CardCount,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics);

public sealed record OblivionPageInfo(
    string Id,
    string Title,
    int CardCount);

public sealed record OblivionCardInfo(
    string Id,
    string PageId,
    string Title,
    string Kind,
    string Status,
    string? ContentSource,
    string ContentSummary);

public sealed record OblivionCardDetail(
    string Id,
    string PageId,
    string Title,
    string? Subtitle,
    string Kind,
    string Status,
    IReadOnlyList<string> Tags,
    string? MarkdownSource,
    string ProvenanceKind,
    string? ProvenanceSource,
    IReadOnlyList<string> Actions,
    string ContentPreview,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics,
    string? DiagramSourceKind = null,
    string? DiagramSourceReference = null,
    string? DiagramSymbol = null,
    string? DiagramProjection = null,
    string? DiagramSemanticFingerprint = null,
    string? DiagramDerivedArtifactStatus = null,
    string? DiagramRenderer = null,
    IReadOnlyList<string>? DiagramCachedAppearances = null,
    string? DiagramRequestedAppearance = null,
    string? DiagramResolvedAppearance = null);

public sealed record OblivionCardContentResult(
    string WorkspaceId,
    string PageId,
    string CardId,
    string ContentKind,
    string Source,
    string Content,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics);

public sealed record OblivionReloadSessionInfo(
    string ActivePageId,
    string? SelectedCardId,
    IReadOnlyList<string> ExpandedCardIds);

public sealed record OblivionWorkspaceReload(
    bool Reloaded,
    OblivionWorkspaceInfo Workspace,
    OblivionReloadSessionInfo Session,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics);

public sealed record OblivionCardStackInfo(
    string Operation,
    string WorkspaceId,
    string PageId,
    string CardId,
    string Title,
    string Kind,
    string Source,
    int OldCount,
    int NewCount,
    string? MetadataPath,
    string? ContentPath,
    bool? ContentDeleted,
    bool Success,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics);

public sealed record OblivionCommandInfo(
    string Id,
    string Title,
    string Description,
    string Scope,
    bool Available);

public sealed record OblivionCommandRunInfo(
    string Id,
    string Title,
    string Scope,
    bool Available,
    bool Executed,
    int AffectedCards,
    OblivionReloadSessionInfo Session,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics);

public sealed record OblivionControlResult<T>(
    T? Value,
    IReadOnlyList<OblivionControlDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Value is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != "error");
}

public sealed class OblivionWorkspaceControl
{
    private const int FormatVersion = 1;
    private const int PreviewLimit = 400;
    private readonly OblivionApplication _application;
    private readonly OblivionCommandRegistry _commands;

    public OblivionWorkspaceControl(
        OblivionApplication? application = null,
        OblivionCommandRegistry? commands = null)
    {
        _application = application ?? new OblivionApplication();
        _commands = commands ?? new OblivionCommandRegistry();
    }

    public OblivionControlResult<OblivionWorkspaceInfo> Show(string workspaceRoot)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionWorkspaceInfo info = CreateWorkspaceInfo(open.Session);
        IReadOnlyList<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            open.Diagnostics,
            open.Session.Workspace);
        return new(info, diagnostics);
    }

    public OblivionWorkspaceValidation Validate(string workspaceRoot)
    {
        OblivionWorkspaceLoadResult load = OblivionApplication.LoadVault(workspaceRoot);
        IReadOnlyList<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            load.Diagnostics,
            load.Workspace);
        return new OblivionWorkspaceValidation(
            load.Succeeded,
            diagnostics.Count(diagnostic => diagnostic.Severity == "error"),
            diagnostics.Count(diagnostic => diagnostic.Severity == "warning"),
            load.Workspace?.Pages.Count ?? 0,
            load.Workspace?.Pages.Sum(page => page.Cards.Count) ?? 0,
            diagnostics);
    }

    public OblivionControlResult<IReadOnlyList<OblivionPageInfo>> ListPages(string workspaceRoot)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionPageInfo[] pages = open.Session.Workspace.Pages
            .Select(page => new OblivionPageInfo(page.Id.Value, page.Title, page.Cards.Count))
            .ToArray();
        return new(pages, ConvertDiagnostics(open.Diagnostics, open.Session.Workspace));
    }

    public OblivionControlResult<IReadOnlyList<OblivionCardInfo>> ListCards(
        string workspaceRoot,
        string? pageId = null)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionWorkspace workspace = open.Session.Workspace;
        IReadOnlyList<OblivionWorkspacePage> pages = workspace.Pages;
        if (pageId is not null)
        {
            OblivionWorkspacePage? page = pages.FirstOrDefault(
                candidate => candidate.Id.Value == pageId);
            if (page is null)
            {
                OblivionControlDiagnostic diagnostic = UnknownPage(workspace, pageId);
                return new(null, [diagnostic]);
            }

            pages = [page];
        }

        OblivionCardInfo[] cards = pages
            .SelectMany(page => page.Cards.Select(card => new OblivionCardInfo(
                card.Id.Value,
                page.Id.Value,
                card.Title,
                OblivionWorkspaceValidator.GetCardKindValue(card.Kind),
                OblivionWorkspaceValidator.GetCardStatusValue(card.Status),
                card.Body.SourceReference,
                Summarize(card.Body.RawText))))
            .ToArray();
        return new(cards, ConvertDiagnostics(open.Diagnostics, workspace));
    }

    public OblivionControlResult<OblivionCardDetail> ShowCard(
        string workspaceRoot,
        string cardId)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionWorkspace workspace = open.Session.Workspace;
        OblivionWorkspacePage? page = workspace.Pages.FirstOrDefault(
            candidate => candidate.Cards.Any(card => card.Id.Value == cardId));
        OblivionCard? card = page?.Cards.FirstOrDefault(candidate => candidate.Id.Value == cardId);
        if (page is null || card is null)
        {
            OblivionControlDiagnostic diagnostic = new(
                "unknown-card",
                "error",
                $"Card '{cardId}' was not found in workspace '{workspace.Id.Value}'.",
                workspace.Id.Value,
                null,
                cardId,
                open.Session.Location.ManifestPath,
                null,
                null);
            return new(null, [diagnostic]);
        }

        List<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            open.Diagnostics,
            workspace).ToList();
        OblivionDiagramProjectionResult? diagramProjection = null;
        string? artifactStatus = null;
        IReadOnlyList<string>? cachedAppearances = null;
        if (card.Kind == OblivionCardKind.Diagram)
        {
            diagramProjection = new OblivionDiagramCardRealizer().Project(
                card,
                open.Session.Location.RootDirectory);
            diagnostics.AddRange(diagramProjection.Diagnostics.Select(diagnostic => new OblivionControlDiagnostic(
                diagnostic.Code,
                diagnostic.Severity.ToString().ToLowerInvariant(),
                diagnostic.Message,
                workspace.Id.Value,
                page.Id.Value,
                card.Id.Value,
                diagnostic.SourcePath,
                null,
                null)));
            artifactStatus = diagramProjection.MermaidSource is null
                ? "projection-failed"
                : ResolveDiagramArtifactStatus(diagramProjection.MermaidSource, out cachedAppearances);
        }
        OblivionConfigResult config = new OblivionConfigStore().Load();
        string? requestedAppearance = card.Kind == OblivionCardKind.Diagram && config.Config is not null
            ? config.Config.Appearance.ToString().ToLowerInvariant()
            : null;
        string? resolvedAppearance = config.Config?.Appearance switch
        {
            OblivionAppearance.Light => "light",
            OblivionAppearance.Dark => "dark",
            _ => null,
        };
        OblivionCardDetail detail = new(
            card.Id.Value,
            page.Id.Value,
            card.Title,
            card.Subtitle,
            OblivionWorkspaceValidator.GetCardKindValue(card.Kind),
            OblivionWorkspaceValidator.GetCardStatusValue(card.Status),
            card.Tags,
            card.Body.SourceReference,
            card.Provenance.SourceKind.ToString(),
            card.Provenance.SourceReference,
            card.Actions.Where(action => action.Enabled).Select(action => action.Id).ToArray(),
            Preview(card.Body.RawText),
            diagnostics,
            card.Diagram?.Kind.ToString(),
            card.Diagram?.Reference,
            card.Diagram?.Symbol,
            card.Diagram?.Projection.ToString(),
            diagramProjection?.SemanticFingerprint,
            artifactStatus,
            card.Kind == OblivionCardKind.Diagram
                ? $"{OblivionMermaidRendererOptions.RendererId}@{OblivionMermaidRendererOptions.PinnedVersion}"
                : null,
            cachedAppearances,
            requestedAppearance,
            resolvedAppearance);
        return new(detail, diagnostics);
    }

    private static string ResolveDiagramArtifactStatus(
        string mermaidSource,
        out IReadOnlyList<string> cachedAppearances)
    {
        string hash = OblivionMermaidHashing.ComputeSourceHash(mermaidSource);
        string outputDirectory = Path.GetFullPath(Path.Combine("artifacts", "derived", "mermaid"));
        List<string> cached = [];
        foreach (OblivionResolvedAppearance appearance in Enum.GetValues<OblivionResolvedAppearance>())
        {
            string path = OblivionMermaidArtifactIdentity.ArtifactPath(
                outputDirectory,
                hash,
                appearance);
            if (File.Exists(path) && File.Exists(Path.ChangeExtension(path, ".json")))
            {
                cached.Add(appearance.ToString().ToLowerInvariant());
            }
        }

        cachedAppearances = cached;
        return cached.Count == 0
            ? "not-realized"
            : $"cached-qualified-artifacts:{string.Join(',', cached)}";
    }

    public OblivionControlResult<OblivionCardContentResult> GetCardContent(
        string workspaceRoot,
        string cardId,
        string? pageId = null)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        return ResolveCardContent(
            open.Session.Workspace,
            open.Session.Location.ManifestPath,
            cardId,
            pageId,
            ConvertDiagnostics(open.Diagnostics, open.Session.Workspace));
    }

    internal static OblivionControlResult<OblivionCardContentResult> ResolveCardContent(
        OblivionWorkspace workspace,
        string manifestPath,
        string cardId,
        string? pageId,
        IReadOnlyList<OblivionControlDiagnostic>? workspaceDiagnostics = null)
    {
        OblivionWorkspacePage? page;
        if (pageId is null)
        {
            page = workspace.Pages.FirstOrDefault(
                candidate => candidate.Cards.Any(card => card.Id.Value == cardId));
        }
        else
        {
            page = workspace.Pages.FirstOrDefault(candidate => candidate.Id.Value == pageId);
            if (page is null)
            {
                return new(null, [UnknownPage(workspace, pageId)]);
            }
        }

        OblivionCard? card = page?.Cards.FirstOrDefault(candidate => candidate.Id.Value == cardId);
        if (page is null || card is null)
        {
            OblivionControlDiagnostic diagnostic = new(
                "unknown-card",
                "error",
                $"Card '{cardId}' was not found in workspace '{workspace.Id.Value}'" +
                (pageId is null ? "." : $" on Page '{pageId}'."),
                workspace.Id.Value,
                pageId,
                cardId,
                manifestPath,
                null,
                null);
            return new(null, [diagnostic]);
        }

        if (card.Body.Format != OblivionCardBodyFormat.CopelandMarkdown ||
            card.Body.Content is not (OblivionInlineMarkdownContent or OblivionMarkdownReferenceContent))
        {
            OblivionControlDiagnostic diagnostic = new(
                "OBLIVION-CARD-CONTENT-NOT-TEXT",
                "error",
                $"Card '{cardId}' does not expose Markdown source content.",
                workspace.Id.Value,
                page.Id.Value,
                card.Id.Value,
                card.Body.SourceReference ?? card.Provenance.SourceReference,
                null,
                null);
            return new(null, [diagnostic]);
        }

        IReadOnlyList<OblivionControlDiagnostic> diagnostics = workspaceDiagnostics ?? [];
        OblivionCardContentResult result = new(
            workspace.Id.Value,
            page.Id.Value,
            card.Id.Value,
            "markdown",
            card.Body.SourceReference ?? "<inline>",
            card.Body.RawText,
            diagnostics);
        return new(result, diagnostics);
    }

    public OblivionControlResult<OblivionWorkspaceReload> Reload(string workspaceRoot)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionWorkspaceSessionReloadResult reload = _application.ReloadWorkspace(open.Session);
        OblivionWorkspaceSession session = reload.Session;
        IReadOnlyList<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            reload.Diagnostics,
            session.Workspace);
        OblivionWorkspaceReload value = new(
            reload.Reloaded,
            CreateWorkspaceInfo(session),
            CreateSessionInfo(session),
            diagnostics);
        return new(value, diagnostics);
    }

    public OblivionControlResult<OblivionCardStackInfo> PushMarkdownCard(
        string workspaceRoot,
        string sourcePath,
        string? pageId,
        string? cardId,
        string? title,
        string? subtitle)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionStackOperationResult operation = _application.PushMarkdownCard(
            open.Session,
            new OblivionPushMarkdownCardRequest(sourcePath, pageId, cardId, title, subtitle));
        return CreateMutationResult(operation);
    }

    public OblivionControlResult<OblivionCardStackInfo> PeekCard(
        string workspaceRoot,
        string? pageId)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionWorkspace workspace = open.Session.Workspace;
        string? targetPageId = pageId ?? workspace.DefaultPageId?.Value;
        OblivionWorkspacePage? page = targetPageId is null
            ? null
            : workspace.Pages.FirstOrDefault(candidate => candidate.Id.Value == targetPageId);
        if (targetPageId is null)
        {
            OblivionControlDiagnostic diagnostic = new(
                "OBLIVION-PAGE-TARGET-REQUIRED",
                "error",
                $"Workspace '{workspace.Id.Value}' has no default Page; provide --page.",
                workspace.Id.Value,
                null,
                null,
                open.Session.Location.ManifestPath,
                null,
                null);
            return new(null, [diagnostic]);
        }

        if (page is null)
        {
            return new(null, [UnknownPage(workspace, targetPageId)]);
        }

        OblivionCard? card = page.Cards.LastOrDefault();
        if (card is null)
        {
            OblivionControlDiagnostic diagnostic = new(
                "OBLIVION-STACK-EMPTY",
                "error",
                $"Page '{targetPageId}' has no top Card to peek.",
                workspace.Id.Value,
                targetPageId,
                null,
                OblivionStructuredVaultPaths.PageMetadata(workspaceRoot, targetPageId),
                null,
                null);
            return new(null, [diagnostic]);
        }

        IReadOnlyList<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            open.Diagnostics,
            workspace);
        OblivionCardStackInfo value = new(
            "peek",
            workspace.Id.Value,
            targetPageId,
            card.Id.Value,
            card.Title,
            OblivionWorkspaceValidator.GetCardKindValue(card.Kind),
            card.Body.SourceReference ?? "<inline>",
            page.Cards.Count,
            page.Cards.Count,
            null,
            card.Body.SourceReference,
            null,
            true,
            diagnostics);
        return new(value, diagnostics);
    }

    public OblivionControlResult<OblivionCardStackInfo> PopCard(
        string workspaceRoot,
        string? pageId)
    {
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionStackOperationResult operation = _application.PopCard(open.Session, pageId);
        return CreateMutationResult(operation);
    }

    public IReadOnlyList<OblivionCommandInfo> ListCommands()
    {
        return _commands.Descriptors.Select(descriptor => new OblivionCommandInfo(
            descriptor.Id,
            descriptor.Title,
            descriptor.Description,
            FormatScope(descriptor.Scope),
            descriptor.Available)).ToArray();
    }

    public OblivionControlResult<OblivionCommandRunInfo> RunCommand(
        string workspaceRoot,
        string externalCommandId)
    {
        if (!_commands.TryResolve(externalCommandId, out OblivionCommandId commandId))
        {
            OblivionControlDiagnostic unknown = new(
                "OBLIVION-COMMAND-UNKNOWN",
                "error",
                $"Command '{externalCommandId}' is not registered.",
                null,
                null,
                null,
                null,
                null,
                null);
            return new(null, [unknown]);
        }

        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(workspaceRoot);
        if (!open.Succeeded || open.Session is null)
        {
            return new(null, ConvertDiagnostics(open.Diagnostics, null));
        }

        OblivionCommandExecutionResult execution = _commands.Run(
            _application,
            open.Session,
            commandId);
        IReadOnlyList<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            execution.Diagnostics,
            execution.Session.Workspace);
        if (!execution.Succeeded || execution.Command is null)
        {
            return new(null, diagnostics);
        }

        OblivionCommandDescriptor descriptor = execution.Command;
        OblivionCommandRunInfo value = new(
            descriptor.Id,
            descriptor.Title,
            FormatScope(descriptor.Scope),
            descriptor.Available,
            execution.Executed,
            execution.AffectedCards,
            CreateSessionInfo(execution.Session),
            diagnostics);
        return new(value, diagnostics);
    }

    private static OblivionControlResult<OblivionCardStackInfo> CreateMutationResult(
        OblivionStackOperationResult operation)
    {
        IReadOnlyList<OblivionControlDiagnostic> diagnostics = ConvertDiagnostics(
            operation.Diagnostics,
            operation.Session.Workspace);
        if (operation.Mutation is null)
        {
            return new(null, diagnostics);
        }

        OblivionStackMutationResult mutation = operation.Mutation;
        OblivionWorkspacePage page = operation.Session.Workspace.Pages.Single(
            candidate => candidate.Id.Value == mutation.PageId);
        OblivionCard? card = page.Cards.FirstOrDefault(
            candidate => candidate.Id.Value == mutation.CardId);
        string title = card?.Title ?? mutation.CardId;
        string kind = card is null
            ? "Markdown"
            : OblivionWorkspaceValidator.GetCardKindValue(card.Kind);
        string source = card?.Body.SourceReference ?? mutation.ContentPath;
        OblivionCardStackInfo value = new(
            mutation.Operation,
            mutation.WorkspaceId,
            mutation.PageId,
            mutation.CardId,
            title,
            kind,
            source,
            mutation.OldCount,
            mutation.NewCount,
            mutation.MetadataPath,
            mutation.ContentPath,
            mutation.Operation == "pop" ? mutation.ContentDeleted : null,
            true,
            diagnostics);
        return new(value, diagnostics);
    }

    private static OblivionWorkspaceInfo CreateWorkspaceInfo(OblivionWorkspaceSession session)
    {
        return new OblivionWorkspaceInfo(
            session.Workspace.Id.Value,
            session.Workspace.Title,
            FormatVersion,
            session.Workspace.DefaultPageId?.Value,
            session.Workspace.Pages.Count,
            session.Workspace.Pages.Sum(page => page.Cards.Count),
            session.Location.RootDirectory);
    }

    private static OblivionReloadSessionInfo CreateSessionInfo(OblivionWorkspaceSession session)
    {
        string pageId = session.ActivePage.Id.Value;
        string? selectedCardId = session.State.GetSelectedCardId(pageId, session.ActivePage.Cards);
        string[] expandedCardIds = session.ActivePage.Cards
            .Where(card => session.State.GetCardViewState(pageId, card.Id.Value).IsExpanded)
            .Select(card => card.Id.Value)
            .ToArray();
        return new OblivionReloadSessionInfo(pageId, selectedCardId, expandedCardIds);
    }

    private static IReadOnlyList<OblivionControlDiagnostic> ConvertDiagnostics(
        IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics,
        OblivionWorkspace? workspace)
    {
        return diagnostics.Select(diagnostic => new OblivionControlDiagnostic(
            diagnostic.Code,
            diagnostic.Severity.ToString().ToLowerInvariant(),
            diagnostic.Message,
            workspace?.Id.Value,
            null,
            null,
            diagnostic.SourcePath,
            diagnostic.Line,
            diagnostic.Column)).ToArray();
    }

    private static OblivionControlDiagnostic UnknownPage(
        OblivionWorkspace workspace,
        string pageId)
    {
        return new OblivionControlDiagnostic(
            "unknown-page",
            "error",
            $"Page '{pageId}' was not found in workspace '{workspace.Id.Value}'.",
            workspace.Id.Value,
            pageId,
            null,
            null,
            null,
            null);
    }

    private static string Summarize(string text)
    {
        string normalized = string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 80 ? normalized : normalized[..77] + "...";
    }

    private static string Preview(string text)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.Length <= PreviewLimit
            ? normalized
            : normalized[..PreviewLimit] + "\n...";
    }

    private static string FormatScope(OblivionCommandScope scope)
    {
        return scope switch
        {
            OblivionCommandScope.Workspace => "workspace",
            OblivionCommandScope.ActivePage => "active-page",
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
    }
}
