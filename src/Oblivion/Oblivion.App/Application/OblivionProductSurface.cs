using System.Text.Json;
using System.Text.Json.Serialization;
using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionProductDiagnostic(
    string Code,
    OblivionDiagnosticSeverity Severity,
    string Message,
    string? WorkspaceId = null,
    string? PageId = null,
    string? CardId = null,
    string? ActionId = null,
    string? EffectKind = null,
    string? ArtifactId = null,
    string? SourceReference = null,
    int? Line = null,
    int? Column = null);

public sealed record OblivionProductSessionSnapshot(
    string Kind,
    string? SelectedPageId,
    string? SelectedCardId);

public sealed record OblivionProductWorkspaceSummary(
    string Id,
    string Title,
    string ManifestPath,
    string RootDirectory,
    string? DefaultPageId,
    IReadOnlyList<OblivionProductPageSummary> Pages);

public sealed record OblivionProductPageSummary(
    string Id,
    string SectionId,
    string Title,
    string? Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> CardIds);

public sealed record OblivionProductCardSummary(
    string Id,
    string PageId,
    string Kind,
    string Status,
    string Title,
    string? SourceReference,
    string? ContentSourceReference,
    int ArtifactCount,
    int AvailableActionCount);

public sealed record OblivionProductBodySnapshot(
    string Format,
    string ContentKind,
    string? SourceReference,
    string Text);

public sealed record OblivionProductDiagramSnapshot(
    string ContentId,
    string Source,
    string? SourceReference,
    string SourceHash,
    string RendererId,
    string RendererVersion,
    string OutputFormat,
    string CacheKey,
    string RendererStatus,
    string? CachedArtifactPath,
    bool CachedArtifactExists);

public sealed record OblivionProductProvenanceSnapshot(
    string SourceKind,
    string? SourceReference,
    string? ProducerActionId,
    string? ParentArtifactId,
    string? ParentCardId);

public sealed record OblivionProductArtifactAddressSnapshot(
    string WorkspaceId,
    string PageId,
    string CardId,
    string ArtifactId);

public sealed record OblivionProductArtifactSnapshot(
    OblivionProductArtifactAddressSnapshot Address,
    string Id,
    string CardId,
    string PageId,
    string WorkspaceId,
    string Label,
    string Kind,
    string? Reference,
    string? ResolvedPath,
    bool Exists,
    bool IsFile,
    bool IsDirectory,
    string? Extension,
    long? ByteLength,
    string? MediaType,
    bool Generated,
    string? SourceReference,
    OblivionProductProvenanceSnapshot Provenance,
    IReadOnlyList<OblivionProductDiagnostic> Diagnostics);

public sealed record OblivionProductDeclaredActionSnapshot(
    string Id,
    string Label,
    bool Enabled);

public sealed record OblivionProductActionSnapshot(
    string Id,
    string Label,
    string Intent,
    string Availability,
    string EffectKind,
    bool RequiresEffect,
    string? HostCapabilityRequired,
    bool SemanticallyInvokable);

public sealed record OblivionProductCardSnapshot(
    string Id,
    string PageId,
    string? WorkspaceId,
    string Kind,
    string Status,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Tags,
    OblivionProductBodySnapshot Body,
    OblivionProductProvenanceSnapshot Provenance,
    IReadOnlyList<OblivionProductDeclaredActionSnapshot> DeclaredActions,
    IReadOnlyList<OblivionProductActionSnapshot> AvailableActions,
    IReadOnlyList<OblivionProductArtifactSnapshot> Artifacts,
    IReadOnlyList<OblivionProductDiagramSnapshot> Diagrams,
    IReadOnlyList<OblivionProductDiagnostic> Diagnostics);

public sealed record OblivionProductWorkspaceSnapshot(
    string SchemaVersion,
    OblivionProductWorkspaceSummary Workspace,
    OblivionProductSessionSnapshot Session,
    IReadOnlyList<OblivionProductCardSummary> Cards,
    IReadOnlyList<OblivionProductDiagnostic> Diagnostics);

public sealed record OblivionProductValidationSnapshot(
    string SchemaVersion,
    string WorkspaceId,
    bool Valid,
    int PageCount,
    int CardCount,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<OblivionProductDiagnostic> Diagnostics);

public sealed record OblivionProductInvocationSnapshot(
    string SchemaVersion,
    string WorkspaceId,
    string PageId,
    string CardId,
    string ActionId,
    string RequestId,
    string EffectKind,
    string Status,
    string Message,
    IReadOnlyList<OblivionProductArtifactSnapshot> Artifacts,
    IReadOnlyList<OblivionProductDiagnostic> Diagnostics);

public sealed record OblivionProductSurfaceResult<T>(
    T? Value,
    IReadOnlyList<OblivionProductDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Value is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != OblivionDiagnosticSeverity.Error);
}

public sealed class OblivionProductSurface
{
    public const string SchemaVersion = "oblivion.product.v1";

    private readonly OblivionCardHandlerRegistry _handlers;
    private readonly OblivionArtifactResolver _artifacts;
    private readonly OblivionLocalHostCapabilities _localHost;

    public OblivionProductSurface(
        OblivionCardHandlerRegistry? handlers = null,
        OblivionLocalHostCapabilities? localHost = null)
    {
        _handlers = handlers ?? OblivionCardHandlerRegistry.CreateDefault();
        _artifacts = new OblivionArtifactResolver();
        _localHost = localHost ?? OblivionLocalHostCapabilities.None;
    }

    public OblivionProductSurfaceResult<OblivionProductWorkspaceSnapshot> Inspect(string manifestPath)
    {
        OblivionWorkspaceLoadResult loadResult = Load(manifestPath);
        if (loadResult.Workspace is null || loadResult.Location is null)
        {
            return new(null, ConvertWorkspaceDiagnostics(loadResult.Diagnostics, null));
        }

        OblivionWorkspace workspace = loadResult.Workspace;
        OblivionProductPageSummary[] pages = workspace.Sections
            .SelectMany(section => section.Pages.Select(page => new OblivionProductPageSummary(
                page.Id.Value,
                section.Id,
                page.Title,
                page.Description,
                page.Tags,
                page.Cards.Select(card => card.Id.Value).ToArray())))
            .ToArray();
        OblivionProductCardSummary[] cards = workspace.Pages
            .SelectMany(page => page.Cards.Select(card => CreateCardSummary(workspace, page, card)))
            .ToArray();
        string? selectedPageId = workspace.DefaultPageId?.Value ?? workspace.Pages.FirstOrDefault()?.Id.Value;
        IReadOnlyList<OblivionProductDiagnostic> diagnostics =
            ConvertWorkspaceDiagnostics(loadResult.Diagnostics, workspace);
        OblivionProductWorkspaceSnapshot snapshot = new(
            SchemaVersion,
            new OblivionProductWorkspaceSummary(
                workspace.Id.Value,
                workspace.Title,
                loadResult.Location.ManifestPath,
                loadResult.Location.RootDirectory,
                workspace.DefaultPageId?.Value,
                pages),
            new OblivionProductSessionSnapshot("initial-session-defaults", selectedPageId, null),
            cards,
            diagnostics);
        return new(snapshot, diagnostics);
    }

    public OblivionProductSurfaceResult<IReadOnlyList<OblivionProductPageSummary>> ListPages(string manifestPath)
    {
        OblivionProductSurfaceResult<OblivionProductWorkspaceSnapshot> result = Inspect(manifestPath);
        return new(result.Value?.Workspace.Pages, result.Diagnostics);
    }

    public OblivionProductSurfaceResult<IReadOnlyList<OblivionProductCardSummary>> ListCards(
        string manifestPath,
        string? pageId = null)
    {
        OblivionProductSurfaceResult<OblivionProductWorkspaceSnapshot> result = Inspect(manifestPath);
        if (result.Value is null)
        {
            return new(null, result.Diagnostics);
        }

        if (pageId is null)
        {
            return new(result.Value.Cards, result.Diagnostics);
        }

        if (!result.Value.Workspace.Pages.Any(page => page.Id == pageId))
        {
            return Failure<IReadOnlyList<OblivionProductCardSummary>>(
                "OBLIVION-PAGE-NOT-FOUND",
                $"Page '{pageId}' was not found in workspace '{result.Value.Workspace.Id}'.",
                result.Value.Workspace.Id,
                pageId: pageId);
        }

        return new(
            result.Value.Cards.Where(card => card.PageId == pageId).ToArray(),
            result.Diagnostics);
    }

    public OblivionProductSurfaceResult<OblivionProductCardSnapshot> ShowCard(
        string manifestPath,
        string cardId)
    {
        OblivionWorkspaceLoadResult loadResult = Load(manifestPath);
        if (loadResult.Workspace is null || loadResult.Location is null)
        {
            return new(null, ConvertWorkspaceDiagnostics(loadResult.Diagnostics, null));
        }

        if (!TryFindCard(loadResult.Workspace, cardId, out OblivionWorkspacePage? page, out OblivionCard? card))
        {
            return Failure<OblivionProductCardSnapshot>(
                "OBLIVION-CARD-NOT-FOUND",
                $"Card '{cardId}' was not found in workspace '{loadResult.Workspace.Id.Value}'.",
                loadResult.Workspace.Id.Value,
                cardId: cardId);
        }

        OblivionWorkspacePage resolvedPage = page!;
        OblivionCard resolvedCard = card!;
        OblivionResolvedArtifact[] resolvedArtifacts = resolvedCard.Artifacts
            .Select(artifact => _artifacts.Resolve(
                loadResult.Workspace,
                loadResult.Location,
                resolvedPage,
                resolvedCard,
                artifact).Artifact!)
            .ToArray();

        OblivionBuiltCard builtCard = _handlers.BuildCard(
            resolvedCard,
            resolvedPage.Id.Value,
            loadResult.Workspace.Id.Value,
            OblivionEffectState.Empty);
        IReadOnlyList<OblivionProductDiagnostic> workspaceDiagnostics =
            ConvertWorkspaceDiagnostics(loadResult.Diagnostics, loadResult.Workspace);
        IReadOnlyList<OblivionProductDiagnostic> diagnostics =
        [
            .. workspaceDiagnostics.Where(diagnostic =>
                diagnostic.CardId is null || diagnostic.CardId == resolvedCard.Id.Value),
            .. builtCard.RuntimeModel.Diagnostics.Select(diagnostic => ConvertCardDiagnostic(
                diagnostic,
                loadResult.Workspace.Id.Value,
                resolvedPage.Id.Value,
                resolvedCard.Id.Value)),
            .. resolvedArtifacts.SelectMany(artifact => artifact.Diagnostics),
        ];
        OblivionProductCardSnapshot snapshot = new(
            resolvedCard.Id.Value,
            resolvedPage.Id.Value,
            loadResult.Workspace.Id.Value,
            EnumValue(resolvedCard.Kind),
            EnumValue(resolvedCard.Status),
            resolvedCard.Title,
            resolvedCard.Subtitle,
            resolvedCard.Tags,
            new OblivionProductBodySnapshot(
                BodyFormat(resolvedCard.Body.Format),
                ContentKind(resolvedCard.Body.Content),
                resolvedCard.Body.SourceReference,
                resolvedCard.Body.RawText),
            new OblivionProductProvenanceSnapshot(
                EnumValue(resolvedCard.Provenance.SourceKind),
                resolvedCard.Provenance.SourceReference,
                resolvedCard.Provenance.ProducerActionId,
                resolvedCard.Provenance.ParentArtifactId?.Value,
                resolvedCard.Provenance.ParentCardId?.Value),
            resolvedCard.Actions.Select(action => new OblivionProductDeclaredActionSnapshot(
                action.Id,
                action.Label,
                action.Enabled)).ToArray(),
            builtCard.RuntimeModel.Actions.Select(CreateActionSnapshot).ToArray(),
            resolvedArtifacts.Select(CreateArtifactSnapshot).ToArray(),
            CreateDiagramSnapshots(resolvedCard),
            diagnostics);
        return new(snapshot, diagnostics);
    }

    private static IReadOnlyList<OblivionProductDiagramSnapshot> CreateDiagramSnapshots(
        OblivionCard card)
    {
        OblivionContentPresentationPlan plan = OblivionContentPresenterSelector.Select(
            card,
            new OblivionCardViewState(true, 0));
        OblivionMermaidRendererOptions options = OblivionMermaidRendererDiscovery.Discover();
        bool rendererInstalled = !string.IsNullOrWhiteSpace(options.ExecutablePath) &&
            File.Exists(options.ExecutablePath) &&
            (options.CliPath is null || File.Exists(options.CliPath));
        string cacheDirectory = Path.GetFullPath(Path.Combine("artifacts", "derived", "mermaid"));

        return plan.Items
            .Where(item => item.PresenterKind == OblivionContentPresenterKind.ExternalMermaidRenderer)
            .Select(item =>
            {
                string sourceHash = OblivionMermaidHashing.ComputeSourceHash(item.Source);
                MermaidDerivedArtifactKey key = new(
                    sourceHash,
                    OblivionMermaidRendererOptions.RendererId,
                    OblivionMermaidRendererOptions.PinnedVersion,
                    OblivionMermaidRendererOptions.OutputFormat,
                    OblivionMermaidRendererOptions.RenderingOptions);
                string cachedPath = Path.Combine(cacheDirectory, key.Value + ".png");
                bool cached = File.Exists(cachedPath) &&
                    File.Exists(Path.ChangeExtension(cachedPath, ".json"));
                string status = cached
                    ? "cached-qualified-artifact"
                    : rendererInstalled
                        ? "installed-awaiting-visual-realization"
                        : "renderer-unavailable-source-retained";
                return new OblivionProductDiagramSnapshot(
                    item.ContentId,
                    item.Source,
                    item.SourceReference,
                    sourceHash,
                    OblivionMermaidRendererOptions.RendererId,
                    OblivionMermaidRendererOptions.PinnedVersion,
                    OblivionMermaidRendererOptions.OutputFormat,
                    key.Value,
                    status,
                    cached ? cachedPath : null,
                    cached);
            })
            .ToArray();
    }

    public OblivionProductSurfaceResult<IReadOnlyList<OblivionProductActionSnapshot>> ListActions(
        string manifestPath,
        string cardId)
    {
        OblivionProductSurfaceResult<OblivionProductCardSnapshot> result = ShowCard(manifestPath, cardId);
        return new(result.Value?.AvailableActions, result.Diagnostics);
    }

    public OblivionProductSurfaceResult<IReadOnlyList<OblivionProductArtifactSnapshot>> ListArtifacts(
        string manifestPath,
        string? cardId = null)
    {
        OblivionWorkspaceLoadResult loadResult = Load(manifestPath);
        if (loadResult.Workspace is null || loadResult.Location is null)
        {
            return new(null, ConvertWorkspaceDiagnostics(loadResult.Diagnostics, null));
        }

        if (cardId is not null && !loadResult.Workspace.Pages
            .SelectMany(page => page.Cards)
            .Any(card => card.Id.Value == cardId))
        {
            return Failure<IReadOnlyList<OblivionProductArtifactSnapshot>>(
                "OBLIVION-CARD-NOT-FOUND",
                $"Card '{cardId}' was not found in workspace '{loadResult.Workspace.Id.Value}'.",
                loadResult.Workspace.Id.Value,
                cardId: cardId);
        }

        OblivionResolvedArtifact[] resolvedArtifacts = loadResult.Workspace.Pages
            .SelectMany(page => page.Cards
                .Where(card => cardId is null || card.Id.Value == cardId)
                .SelectMany(card => card.Artifacts.Select(
                artifact => _artifacts.Resolve(
                    loadResult.Workspace,
                    loadResult.Location,
                    page,
                    card,
                    artifact).Artifact!)))
            .ToArray();
        IReadOnlyList<OblivionProductDiagnostic> diagnostics =
        [
            .. ConvertWorkspaceDiagnostics(loadResult.Diagnostics, loadResult.Workspace),
            .. resolvedArtifacts.SelectMany(artifact => artifact.Diagnostics),
        ];
        return new(resolvedArtifacts.Select(CreateArtifactSnapshot).ToArray(), diagnostics);
    }

    public OblivionProductSurfaceResult<OblivionProductArtifactSnapshot> ShowArtifact(
        string manifestPath,
        string cardId,
        string artifactId)
    {
        OblivionWorkspaceLoadResult loadResult = Load(manifestPath);
        if (loadResult.Workspace is null || loadResult.Location is null)
        {
            return new(null, ConvertWorkspaceDiagnostics(loadResult.Diagnostics, loadResult.Workspace));
        }

        OblivionArtifactResolutionResult resolution = _artifacts.Resolve(
            loadResult.Workspace,
            loadResult.Location,
            cardId,
            artifactId);
        if (resolution.Artifact is null)
        {
            return new(null, resolution.Diagnostics);
        }

        return new(CreateArtifactSnapshot(resolution.Artifact), resolution.Diagnostics);
    }

    public OblivionProductSurfaceResult<OblivionProductValidationSnapshot> Validate(string manifestPath)
    {
        OblivionWorkspaceLoadResult loadResult = Load(manifestPath);
        if (loadResult.Workspace is null)
        {
            return new(null, ConvertWorkspaceDiagnostics(loadResult.Diagnostics, null));
        }

        IReadOnlyList<OblivionProductDiagnostic> diagnostics =
            ConvertWorkspaceDiagnostics(loadResult.Diagnostics, loadResult.Workspace);
        OblivionProductValidationSnapshot snapshot = new(
            SchemaVersion,
            loadResult.Workspace.Id.Value,
            loadResult.Succeeded,
            loadResult.Workspace.Pages.Count,
            loadResult.Workspace.Pages.Sum(page => page.Cards.Count),
            diagnostics.Count(diagnostic => diagnostic.Severity == OblivionDiagnosticSeverity.Error),
            diagnostics.Count(diagnostic => diagnostic.Severity == OblivionDiagnosticSeverity.Warning),
            diagnostics);
        return new(snapshot, diagnostics);
    }

    public OblivionProductSurfaceResult<OblivionProductInvocationSnapshot> Invoke(
        string manifestPath,
        string cardId,
        string actionId,
        string? artifactId = null)
    {
        OblivionWorkspaceLoadResult loadResult = Load(manifestPath);
        if (loadResult.Workspace is null || loadResult.Location is null)
        {
            return new(null, ConvertWorkspaceDiagnostics(loadResult.Diagnostics, null));
        }

        if (!TryFindCard(loadResult.Workspace, cardId, out OblivionWorkspacePage? page, out OblivionCard? card))
        {
            return Failure<OblivionProductInvocationSnapshot>(
                "OBLIVION-CARD-NOT-FOUND",
                $"Card '{cardId}' was not found in workspace '{loadResult.Workspace.Id.Value}'.",
                loadResult.Workspace.Id.Value,
                cardId: cardId,
                actionId: actionId);
        }

        OblivionWorkspacePage resolvedPage = page!;
        OblivionCard resolvedCard = card!;

        OblivionBuiltCard builtCard = _handlers.BuildCard(
            resolvedCard,
            resolvedPage.Id.Value,
            loadResult.Workspace.Id.Value);
        OblivionCardActionDescriptor? action = builtCard.RuntimeModel.Actions.FirstOrDefault(
            candidate => candidate.Id == actionId);
        if (action is null)
        {
            return Failure<OblivionProductInvocationSnapshot>(
                "OBLIVION-ACTION-NOT-FOUND",
                $"Action '{actionId}' is not available for card '{cardId}'. Use 'actions {cardId}' to discover actions.",
                loadResult.Workspace.Id.Value,
                resolvedPage.Id.Value,
                cardId,
                actionId);
        }

        OblivionHostCapabilities capabilities = CreateEffectCapabilities(
            manifestPath,
            loadResult.Workspace,
            loadResult.Location,
            resolvedPage,
            resolvedCard,
            artifactId);
        OblivionApplication application = new(
            _handlers,
            new OblivionCardEffectRouter(capabilities));
        OblivionActionOutcome? outcome = application.Invoke(
            resolvedCard,
            resolvedPage.Id.Value,
            action.ActionId,
            OblivionApplicationState.Empty);
        if (outcome is null)
        {
            return Failure<OblivionProductInvocationSnapshot>(
                "OBLIVION-ACTION-NOT-INVOKABLE",
                $"Action '{actionId}' on card '{cardId}' does not produce a semantic effect request.",
                loadResult.Workspace.Id.Value,
                resolvedPage.Id.Value,
                cardId,
                actionId,
                EnumValue(action.EffectKind));
        }

        OblivionProductDiagnostic[] diagnostics = outcome.Result.Diagnostics
            .Select(diagnostic => ConvertCardDiagnostic(
                diagnostic,
                loadResult.Workspace.Id.Value,
                resolvedPage.Id.Value,
                resolvedCard.Id.Value,
                actionId,
                EnumValue(outcome.Request.Kind),
                artifactId))
            .ToArray();
        OblivionProductArtifactSnapshot[] artifacts = [];
        OblivionProductInvocationSnapshot snapshot = new(
            SchemaVersion,
            loadResult.Workspace.Id.Value,
            resolvedPage.Id.Value,
            resolvedCard.Id.Value,
            actionId,
            outcome.Request.RequestId,
            EnumValue(outcome.Request.Kind),
            EnumValue(outcome.Result.Status),
            outcome.Result.Message,
            artifacts,
            diagnostics);
        return new(snapshot, diagnostics);
    }

    private OblivionHostCapabilities CreateEffectCapabilities(
        string manifestPath,
        OblivionWorkspace workspace,
        OblivionWorkspaceLocation location,
        OblivionWorkspacePage page,
        OblivionCard card,
        string? artifactId)
    {
        return new OblivionHostCapabilities(
            RefreshContent: request => ReloadWorkspace(manifestPath, request),
            OpenSource: _localHost.OpenPath is null
                ? null
                : request => OpenSource(workspace, location, page, card, request),
            CopySourcePath: _localHost.CopyText is null
                ? null
                : request => CopySourcePath(workspace, location, page, card, request),
            OpenArtifact: _localHost.OpenPath is null
                ? null
                : request => OpenArtifact(workspace, location, page, card, artifactId, request));
    }

    private OblivionEffectResult OpenSource(
        OblivionWorkspace workspace,
        OblivionWorkspaceLocation location,
        OblivionWorkspacePage page,
        OblivionCard card,
        OpenSourceEffectRequest request)
    {
        string? reference = card.Body.SourceReference ?? card.Provenance.SourceReference;
        if (!TryResolveExistingFile(
            workspace,
            location,
            page,
            card,
            reference,
            request,
            out string? path,
            out OblivionEffectResult? failure))
        {
            return failure!;
        }

        OblivionOpenPathCapabilityRequest hostRequest = new(
            request.RequestId,
            workspace.Id.Value,
            page.Id.Value,
            card.Id.Value,
            request.Context.ActionId.Value,
            request.Kind,
            OblivionHostPathTargetKind.Source,
            reference!,
            path!);
        return ToEffectResult(request, _localHost.OpenPath!(hostRequest), path!);
    }

    private OblivionEffectResult CopySourcePath(
        OblivionWorkspace workspace,
        OblivionWorkspaceLocation location,
        OblivionWorkspacePage page,
        OblivionCard card,
        CopySourcePathEffectRequest request)
    {
        string? reference = card.Body.SourceReference ?? card.Provenance.SourceReference;
        if (!TryResolveExistingFile(
            workspace,
            location,
            page,
            card,
            reference,
            request,
            out string? path,
            out OblivionEffectResult? failure))
        {
            return failure!;
        }

        OblivionCopyTextCapabilityRequest hostRequest = new(
            request.RequestId,
            workspace.Id.Value,
            page.Id.Value,
            card.Id.Value,
            request.Context.ActionId.Value,
            request.Kind,
            path!,
            "resolved-source-path");
        return ToEffectResult(request, _localHost.CopyText!(hostRequest), path!);
    }

    private OblivionEffectResult OpenArtifact(
        OblivionWorkspace workspace,
        OblivionWorkspaceLocation location,
        OblivionWorkspacePage page,
        OblivionCard card,
        string? artifactId,
        OpenArtifactEffectRequest request)
    {
        if (artifactId is null && card.Artifacts.Count != 1)
        {
            return Rejected(
                request,
                "OBLIVION-ARTIFACT-ID-AMBIGUOUS",
                $"Card '{card.Id.Value}' has {card.Artifacts.Count} artifacts. Supply an artifact id explicitly.",
                card.Provenance.SourceReference);
        }

        string selectedArtifactId = artifactId ?? card.Artifacts[0].Id;
        OblivionArtifactResolutionResult resolution = _artifacts.Resolve(
            workspace,
            location,
            card.Id.Value,
            selectedArtifactId);
        if (!resolution.Succeeded || resolution.Artifact is null)
        {
            OblivionProductDiagnostic diagnostic = resolution.Diagnostics.First();
            return Rejected(
                request,
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.SourceReference);
        }

        OblivionResolvedArtifact artifact = resolution.Artifact;
        if (!artifact.IsFile)
        {
            return Rejected(
                request,
                artifact.IsDirectory
                    ? "OBLIVION-ARTIFACT-NOT-A-FILE"
                    : "OBLIVION-ARTIFACT-NOT-FOUND",
                artifact.IsDirectory
                    ? $"Artifact '{selectedArtifactId}' resolves to a directory, not a file."
                    : $"Artifact '{selectedArtifactId}' does not exist.",
                artifact.ResolvedPath ?? artifact.DeclaredReference);
        }

        OblivionOpenPathCapabilityRequest hostRequest = new(
            request.RequestId,
            workspace.Id.Value,
            page.Id.Value,
            card.Id.Value,
            request.Context.ActionId.Value,
            request.Kind,
            OblivionHostPathTargetKind.Artifact,
            artifact.DeclaredReference!,
            artifact.ResolvedPath!,
            artifact.Address);
        return ToEffectResult(request, _localHost.OpenPath!(hostRequest), artifact.ResolvedPath!);
    }

    private static bool TryResolveExistingFile(
        OblivionWorkspace workspace,
        OblivionWorkspaceLocation location,
        OblivionWorkspacePage page,
        OblivionCard card,
        string? reference,
        OblivionEffectRequest request,
        out string? resolvedPath,
        out OblivionEffectResult? failure)
    {
        resolvedPath = null;
        failure = null;
        if (string.IsNullOrWhiteSpace(reference))
        {
            failure = Rejected(
                request,
                "OBLIVION-SOURCE-NOT-FOUND",
                $"Card '{card.Id.Value}' has no source reference.",
                null);
            return false;
        }

        if (Path.IsPathRooted(reference))
        {
            failure = Rejected(
                request,
                "OBLIVION-SOURCE-PATH-UNSAFE",
                $"Source reference '{reference}' is absolute; source paths must be workspace-relative.",
                reference);
            return false;
        }

        string fullRoot = Path.GetFullPath(location.RootDirectory);
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, reference));
        string relative = Path.GetRelativePath(fullRoot, candidate);
        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            failure = Rejected(
                request,
                "OBLIVION-SOURCE-PATH-UNSAFE",
                $"Source reference '{reference}' escapes workspace '{workspace.Id.Value}'.",
                reference);
            return false;
        }

        if (!File.Exists(candidate))
        {
            failure = Rejected(
                request,
                "OBLIVION-SOURCE-NOT-FOUND",
                $"Source '{reference}' for card '{card.Id.Value}' was not found.",
                candidate);
            return false;
        }

        resolvedPath = candidate;
        return true;
    }

    private static OblivionEffectResult ToEffectResult(
        OblivionEffectRequest request,
        OblivionHostCapabilityResult hostResult,
        string sourcePath)
    {
        if (hostResult.Succeeded)
        {
            return new CompletedEffectResult(
                request.RequestId,
                request.CardId,
                request.Kind,
                hostResult.Message,
                [],
                []);
        }

        return Rejected(
            request,
            hostResult.DiagnosticCode ?? "OBLIVION-HOST-CAPABILITY-FAILED",
            hostResult.Message,
            sourcePath);
    }

    private static OblivionEffectResult Rejected(
        OblivionEffectRequest request,
        string code,
        string message,
        string? sourcePath)
    {
        return new RejectedEffectResult(
            request.RequestId,
            request.CardId,
            request.Kind,
            message,
            [new OblivionCardDiagnostic(code, OblivionDiagnosticSeverity.Error, message, sourcePath)],
            []);
    }

    private static OblivionEffectResult ReloadWorkspace(
        string manifestPath,
        RefreshContentEffectRequest request)
    {
        OblivionWorkspaceLoadResult reload = OblivionWorkspaceApplication.Load(manifestPath, useCache: false);
        OblivionCardDiagnostic[] diagnostics = reload.Diagnostics
            .Select(diagnostic => new OblivionCardDiagnostic(
                diagnostic.Code,
                diagnostic.Severity,
                diagnostic.Message,
                diagnostic.SourcePath,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.SpanStart,
                diagnostic.SpanLength,
                diagnostic.DisplaySeverity))
            .ToArray();
        if (!reload.Succeeded || reload.Workspace is null)
        {
            return new RejectedEffectResult(
                request.RequestId,
                request.CardId,
                request.Kind,
                "Workspace reload failed validation.",
                diagnostics,
                []);
        }

        return new CompletedEffectResult(
            request.RequestId,
            request.CardId,
            request.Kind,
            $"Workspace '{reload.Workspace.Id.Value}' reloaded and validated.",
            [],
            []);
    }

    private OblivionProductCardSummary CreateCardSummary(
        OblivionWorkspace workspace,
        OblivionWorkspacePage page,
        OblivionCard card)
    {
        OblivionBuiltCard builtCard = _handlers.BuildCard(card, page.Id.Value, workspace.Id.Value);
        return new OblivionProductCardSummary(
            card.Id.Value,
            page.Id.Value,
            EnumValue(card.Kind),
            EnumValue(card.Status),
            card.Title,
            card.Provenance.SourceReference,
            card.Body.SourceReference,
            card.Artifacts.Count,
            builtCard.RuntimeModel.Actions.Count);
    }

    private static OblivionProductActionSnapshot CreateActionSnapshot(OblivionCardActionDescriptor action)
    {
        return new OblivionProductActionSnapshot(
            action.Id,
            action.Label,
            action.Intent,
            EnumValue(action.Availability),
            EnumValue(action.EffectKind),
            action.RequiresEffect,
            RequiredHostCapability(action.EffectKind),
            action.RequiresEffect);
    }

    private static string? RequiredHostCapability(OblivionCardEffectKind effectKind)
    {
        return effectKind switch
        {
            OblivionCardEffectKind.RefreshMarkdown => "refresh-content",
            OblivionCardEffectKind.OpenSource => "open-source",
            OblivionCardEffectKind.CopySourcePath => "copy-source-path",
            OblivionCardEffectKind.OpenArtifact => "open-artifact",
            OblivionCardEffectKind.ExportCard => "export-card",
            OblivionCardEffectKind.RenderPreview => "render-preview",
            _ => null,
        };
    }

    private static OblivionProductArtifactSnapshot CreateArtifactSnapshot(
        OblivionResolvedArtifact artifact)
    {
        return new OblivionProductArtifactSnapshot(
            new OblivionProductArtifactAddressSnapshot(
                artifact.Address.WorkspaceId.Value,
                artifact.Address.PageId.Value,
                artifact.Address.CardId.Value,
                artifact.Address.ArtifactId.Value),
            artifact.Address.ArtifactId.Value,
            artifact.Address.CardId.Value,
            artifact.Address.PageId.Value,
            artifact.Address.WorkspaceId.Value,
            artifact.Label,
            artifact.Kind,
            artifact.DeclaredReference,
            artifact.ResolvedPath,
            artifact.Exists,
            artifact.IsFile,
            artifact.IsDirectory,
            artifact.Extension,
            artifact.ByteLength,
            artifact.MediaType,
            artifact.Generated,
            artifact.DeclarationSourceReference,
            new OblivionProductProvenanceSnapshot(
                EnumValue(artifact.Provenance.SourceKind),
                artifact.Provenance.SourceReference,
                artifact.Provenance.ProducerActionId,
                artifact.Provenance.ParentArtifactId?.Value,
                artifact.Provenance.ParentCardId?.Value),
            artifact.Diagnostics);
    }

    private static bool TryFindCard(
        OblivionWorkspace workspace,
        string cardId,
        out OblivionWorkspacePage? page,
        out OblivionCard? card)
    {
        foreach (OblivionWorkspacePage candidatePage in workspace.Pages)
        {
            OblivionCard? candidateCard = candidatePage.Cards.FirstOrDefault(
                candidate => candidate.Id.Value == cardId);
            if (candidateCard is not null)
            {
                page = candidatePage;
                card = candidateCard;
                return true;
            }
        }

        page = null;
        card = null;
        return false;
    }

    private static OblivionWorkspaceLoadResult Load(string manifestPath)
    {
        return OblivionWorkspaceApplication.Load(Path.GetFullPath(manifestPath), useCache: false);
    }

    private static IReadOnlyList<OblivionProductDiagnostic> ConvertWorkspaceDiagnostics(
        IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics,
        OblivionWorkspace? workspace)
    {
        return diagnostics.Select(diagnostic =>
        {
            (string? pageId, string? cardId) = FindDiagnosticOwner(workspace, diagnostic.SourcePath);
            return new OblivionProductDiagnostic(
                diagnostic.Code,
                diagnostic.Severity,
                diagnostic.Message,
                workspace?.Id.Value,
                pageId,
                cardId,
                SourceReference: diagnostic.SourcePath,
                Line: diagnostic.Line,
                Column: diagnostic.Column);
        }).ToArray();
    }

    private static (string? PageId, string? CardId) FindDiagnosticOwner(
        OblivionWorkspace? workspace,
        string? sourceReference)
    {
        if (workspace is null || string.IsNullOrWhiteSpace(sourceReference))
        {
            return (null, null);
        }

        string normalized = sourceReference.Replace('\\', '/');
        foreach (OblivionWorkspacePage page in workspace.Pages)
        {
            foreach (OblivionCard card in page.Cards)
            {
                if (ReferenceMatches(normalized, card.Provenance.SourceReference) ||
                    ReferenceMatches(normalized, card.Body.SourceReference) ||
                    card.Artifacts.Any(artifact => ReferenceMatches(normalized, artifact.SourceReference)))
                {
                    return (page.Id.Value, card.Id.Value);
                }
            }
        }

        return (null, null);
    }

    private static bool ReferenceMatches(string normalizedSource, string? candidate)
    {
        return !string.IsNullOrWhiteSpace(candidate) &&
            normalizedSource.EndsWith(candidate.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    private static OblivionProductDiagnostic ConvertCardDiagnostic(
        OblivionCardDiagnostic diagnostic,
        string workspaceId,
        string pageId,
        string cardId,
        string? actionId = null,
        string? effectKind = null,
        string? artifactId = null)
    {
        return new OblivionProductDiagnostic(
            diagnostic.Code,
            diagnostic.Severity,
            diagnostic.Message,
            workspaceId,
            pageId,
            cardId,
            actionId,
            effectKind,
            artifactId,
            SourceReference: diagnostic.SourcePath,
            Line: diagnostic.Line,
            Column: diagnostic.Column);
    }

    private static OblivionProductSurfaceResult<T> Failure<T>(
        string code,
        string message,
        string? workspaceId = null,
        string? pageId = null,
        string? cardId = null,
        string? actionId = null,
        string? effectKind = null)
    {
        return new(
            default,
            [new OblivionProductDiagnostic(
                code,
                OblivionDiagnosticSeverity.Error,
                message,
                workspaceId,
                pageId,
                cardId,
                actionId,
                effectKind)]);
    }

    private static string ContentKind(OblivionCardContent content)
    {
        return content switch
        {
            OblivionPlainTextContent => "plain-text",
            OblivionInlineMarkdownContent => "inline-markdown",
            OblivionMarkdownReferenceContent => "markdown-reference",
            OblivionArtifactContent => "artifact-reference",
            _ => "unknown",
        };
    }

    private static string BodyFormat(OblivionCardBodyFormat format)
    {
        return format == OblivionCardBodyFormat.CopelandMarkdown
            ? "copeland-markdown"
            : "plain";
    }

    private static string EnumValue<T>(T value)
        where T : struct, Enum
    {
        return JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    }
}

public static class OblivionProductJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }
}
