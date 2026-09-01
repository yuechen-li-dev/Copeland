using System.Diagnostics;
using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionApplicationState(
    OblivionEffectState EffectState)
{
    public static OblivionApplicationState Empty { get; } = new(OblivionEffectState.Empty);

    public OblivionApplicationState Apply(
        OblivionEffectRequest request,
        OblivionEffectResult result)
    {
        return this with
        {
            EffectState = EffectState.WithOutcome(request, result),
        };
    }
}

public sealed record OblivionActionOutcome(
    OblivionEffectRequest Request,
    OblivionEffectResult Result,
    OblivionApplicationState State);

public sealed record OblivionWorkspaceSession(
    OblivionWorkspace Workspace,
    OblivionWorkspacePage ActivePage,
    OblivionSessionState State,
    OblivionWorkspaceLocation Location);

public sealed record OblivionWorkspaceSessionOpenResult(
    OblivionWorkspaceSession? Session,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Session is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != OblivionDiagnosticSeverity.Error);
}

public sealed record OblivionWorkspaceSessionReloadResult(
    OblivionWorkspaceSession Session,
    bool Reloaded,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Reloaded &&
        Diagnostics.All(diagnostic => diagnostic.Severity != OblivionDiagnosticSeverity.Error);
}

public sealed record OblivionPushMarkdownCardRequest(
    string SourcePath,
    string? PageId = null,
    string? CardId = null,
    string? Title = null,
    string? Subtitle = null);

public sealed record OblivionStackOperationResult(
    OblivionWorkspaceSession Session,
    OblivionStackMutationResult? Mutation,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Mutation is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != OblivionDiagnosticSeverity.Error);
}

public sealed record OblivionFunctionRunPreparation(
    OblivionWorkspaceSession Session,
    OblivionCard? Card,
    OblivionFunctionDiscoveryResult Discovery)
{
    public bool Succeeded => Card is not null && Discovery.Succeeded;
}

public sealed class OblivionApplication
{
    private readonly OblivionCardHandlerRegistry _handlers;
    private readonly OblivionCardEffectRouter _effects;
    private readonly OblivionConfigStore _configStore;
    private readonly IOblivionFunctionRunner _functionRunner;

    public OblivionApplication(
        OblivionCardHandlerRegistry? handlers = null,
        OblivionCardEffectRouter? effects = null,
        OblivionConfigStore? configStore = null,
        IOblivionFunctionRunner? functionRunner = null)
    {
        _handlers = handlers ?? OblivionCardHandlerRegistry.CreateDefault();
        _effects = effects ?? new OblivionCardEffectRouter();
        _configStore = configStore ?? new OblivionConfigStore();
        _functionRunner = functionRunner ?? new OblivionXunitFunctionRunner();
    }

    public OblivionFunctionDiscoveryResult InspectFunctionCard(
        OblivionWorkspaceSession session,
        string cardId)
    {
        ArgumentNullException.ThrowIfNull(session);
        OblivionCard? card = session.Workspace.Pages
            .SelectMany(page => page.Cards)
            .FirstOrDefault(candidate => candidate.Id.Value == cardId);
        if (card is null)
        {
            OblivionCardDiagnostic diagnostic = new(
                "OBLIVION-FUNCTION-CARD-NOT-FOUND",
                OblivionDiagnosticSeverity.Error,
                $"Function Card '{cardId}' was not found.",
                session.Location.ManifestPath);
            return new OblivionFunctionDiscoveryResult(null, TimeSpan.Zero, TimeSpan.Zero, [diagnostic]);
        }

        return _functionRunner.Discover(card, session.Location.RootDirectory);
    }

    public OblivionFunctionRunPreparation BeginFunctionCardRun(
        OblivionWorkspaceSession session,
        string cardId)
    {
        OblivionFunctionDiscoveryResult discovery = InspectFunctionCard(session, cardId);
        OblivionCard? card = session.Workspace.Pages
            .SelectMany(page => page.Cards)
            .FirstOrDefault(candidate => candidate.Id.Value == cardId);
        if (!discovery.Succeeded || discovery.Descriptor is null || card is null)
        {
            return new OblivionFunctionRunPreparation(session, card, discovery);
        }

        OblivionFunctionExecutionResult running = OblivionFunctionExecutionResult.Running(
            cardId,
            discovery.Descriptor);
        OblivionWorkspaceSession next = session with
        {
            State = session.State.WithFunctionExecution(cardId, running),
        };
        return new OblivionFunctionRunPreparation(next, card, discovery);
    }

    public OblivionFunctionRunResult CompleteFunctionCardRun(OblivionFunctionRunPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (!preparation.Succeeded || preparation.Card is null || preparation.Discovery.Descriptor is null)
        {
            string cardId = preparation.Card?.Id.Value ?? "<unknown>";
            OblivionFunctionExecutionResult error = new(
                cardId,
                preparation.Card?.Function?.Test ?? "<unknown>",
                preparation.Card?.Function?.Test ?? "<unknown>",
                OblivionFunctionExecutionOutcome.Error,
                null,
                null,
                preparation.Card?.Function?.Reference ?? string.Empty,
                string.Empty,
                OblivionXunitFunctionRunner.RunnerIdentity,
                0,
                0,
                0,
                0,
                DateTimeOffset.UtcNow,
                preparation.Discovery.Diagnostics);
            OblivionWorkspaceSession failedSession = preparation.Session with
            {
                State = preparation.Session.State.WithFunctionExecution(cardId, error),
            };
            return new OblivionFunctionRunResult(
                failedSession,
                null,
                error,
                preparation.Discovery.BuildDuration,
                preparation.Discovery.DiscoveryDuration,
                TimeSpan.Zero);
        }

        Stopwatch runnerClock = Stopwatch.StartNew();
        OblivionFunctionExecutionResult result = _functionRunner.Run(
            preparation.Card,
            preparation.Session.Location.RootDirectory,
            preparation.Discovery.Descriptor);
        runnerClock.Stop();
        OblivionWorkspaceSession session = preparation.Session with
        {
            State = preparation.Session.State.WithFunctionExecution(preparation.Card.Id.Value, result),
        };
        return new OblivionFunctionRunResult(
            session,
            preparation.Discovery.Descriptor,
            result,
            preparation.Discovery.BuildDuration,
            preparation.Discovery.DiscoveryDuration,
            runnerClock.Elapsed);
    }

    public OblivionFunctionRunResult RunFunctionCard(
        OblivionWorkspaceSession session,
        string cardId)
    {
        return CompleteFunctionCardRun(BeginFunctionCardRun(session, cardId));
    }

    public OblivionHostCapabilityResult OpenFunctionSource(
        OblivionWorkspaceSession session,
        string cardId,
        OblivionLocalHostCapabilities host)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(host);
        OblivionCard? card = session.Workspace.Pages
            .SelectMany(page => page.Cards)
            .FirstOrDefault(candidate => candidate.Id.Value == cardId);
        if (card?.Kind != OblivionCardKind.Function || card.Function is null)
        {
            return new OblivionHostCapabilityResult(
                false,
                $"Card '{cardId}' is not a Function Card.",
                "OBLIVION-FUNCTION-CARD-REQUIRED");
        }
        if (host.OpenPath is null)
        {
            return new OblivionHostCapabilityResult(
                false,
                "The local host cannot open source paths.",
                "OBLIVION-HOST-CAPABILITY-UNAVAILABLE");
        }

        string root = Path.GetFullPath(session.Location.RootDirectory);
        string sourcePath = Path.GetFullPath(Path.Combine(root, card.Function.Reference));
        string relative = Path.GetRelativePath(root, sourcePath);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(sourcePath))
        {
            return new OblivionHostCapabilityResult(
                false,
                $"Function source '{card.Function.Reference}' is unavailable or unsafe.",
                "OBLIVION-FUNCTION-SOURCE-UNAVAILABLE");
        }

        string pageId = card.PageId?.Value ?? session.ActivePage.Id.Value;
        return host.OpenPath(new OblivionOpenPathCapabilityRequest(
            Guid.NewGuid().ToString("N"),
            session.Workspace.Id.Value,
            pageId,
            card.Id.Value,
            "open-source",
            OblivionCardEffectKind.OpenSource,
            OblivionHostPathTargetKind.Source,
            card.Function.Reference,
            sourcePath));
    }

    public OblivionActionOutcome? Invoke(
        OblivionCard card,
        string pageId,
        OblivionProductActionId actionId,
        OblivionApplicationState? state = null)
    {
        OblivionApplicationState current = state ?? OblivionApplicationState.Empty;
        OblivionEffectRequest? request = _handlers.CreateEffectRequest(
            card,
            pageId,
            actionId.Value,
            card.WorkspaceId?.Value,
            current.EffectState);
        if (request is null)
        {
            return null;
        }

        OblivionEffectResult result = _effects.Route(request);
        return new OblivionActionOutcome(request, result, current.Apply(request, result));
    }

    public OblivionActionOutcome? Invoke(
        OblivionCard card,
        string pageId,
        string actionId,
        OblivionApplicationState? state = null)
    {
        return Invoke(card, pageId, new OblivionProductActionId(actionId), state);
    }

    public OblivionWorkspaceSessionOpenResult OpenWorkspace(string vaultRoot)
    {
        OblivionWorkspaceLoadResult load = LoadVault(vaultRoot);
        if (!load.Succeeded || load.Workspace is null || load.Location is null)
        {
            return new OblivionWorkspaceSessionOpenResult(null, load.Diagnostics);
        }

        OblivionWorkspace workspace = load.Workspace;
        OblivionWorkspacePage? activePage = workspace.DefaultPageId is null
            ? workspace.Pages.FirstOrDefault()
            : workspace.Pages.FirstOrDefault(page => page.Id == workspace.DefaultPageId);
        if (activePage is null)
        {
            List<OblivionWorkspaceDiagnostic> diagnostics = load.Diagnostics.ToList();
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "active-page-not-found",
                $"Workspace '{workspace.Id.Value}' has no materialized default page.",
                load.Location.ManifestPath));
            return new OblivionWorkspaceSessionOpenResult(
                null,
                OblivionWorkspaceValidator.OrderDiagnostics(diagnostics));
        }

        OblivionSessionState state = OblivionSessionState.Empty.ReconcilePage(
            activePage.Id.Value,
            activePage.Cards);
        OblivionWorkspaceSession session = new(
            workspace,
            activePage,
            state,
            load.Location);
        return new OblivionWorkspaceSessionOpenResult(session, load.Diagnostics);
    }

    public OblivionWorkspaceSessionReloadResult ReloadWorkspace(OblivionWorkspaceSession current)
    {
        ArgumentNullException.ThrowIfNull(current);

        OblivionWorkspaceLoadResult candidate = LoadVault(
            current.Location.RootDirectory);
        if (!candidate.Succeeded || candidate.Workspace is null || candidate.Location is null)
        {
            return new OblivionWorkspaceSessionReloadResult(
                current,
                Reloaded: false,
                candidate.Diagnostics);
        }

        OblivionWorkspace workspace = candidate.Workspace;
        OblivionWorkspacePage? activePage = workspace.Pages.FirstOrDefault(
            page => page.Id == current.ActivePage.Id);
        activePage ??= workspace.DefaultPageId is null
            ? null
            : workspace.Pages.FirstOrDefault(page => page.Id == workspace.DefaultPageId);
        activePage ??= workspace.Pages.FirstOrDefault();
        if (activePage is null)
        {
            List<OblivionWorkspaceDiagnostic> diagnostics = candidate.Diagnostics.ToList();
            diagnostics.Add(OblivionWorkspaceValidator.Error(
                "active-page-not-found",
                $"Workspace '{workspace.Id.Value}' has no materialized page after reload.",
                candidate.Location.ManifestPath));
            return new OblivionWorkspaceSessionReloadResult(
                current,
                Reloaded: false,
                OblivionWorkspaceValidator.OrderDiagnostics(diagnostics));
        }

        OblivionSessionState reconciled = ReconcileSession(current.State, workspace);
        OblivionWorkspaceSession next = new(
            workspace,
            activePage,
            reconciled,
            candidate.Location);
        return new OblivionWorkspaceSessionReloadResult(
            next,
            Reloaded: true,
            candidate.Diagnostics);
    }

    public OblivionStackOperationResult PushMarkdownCard(
        OblivionWorkspaceSession current,
        OblivionPushMarkdownCardRequest request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetNewlinePolicy(current, out OblivionVaultNewlinePolicy newlinePolicy, out IReadOnlyList<OblivionWorkspaceDiagnostic> configDiagnostics))
        {
            return new OblivionStackOperationResult(current, null, configDiagnostics);
        }

        OblivionStackMutationResult? mutation = OblivionStackMutation.PushMarkdown(
            current.Location.RootDirectory,
            request.SourcePath,
            request.PageId,
            request.CardId,
            request.Title,
            request.Subtitle,
            newlinePolicy,
            out IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics);
        if (mutation is null)
        {
            return new OblivionStackOperationResult(current, null, diagnostics);
        }

        OblivionWorkspaceSessionReloadResult reload = ReloadWorkspace(current);
        return new OblivionStackOperationResult(
            reload.Session,
            mutation,
            diagnostics.Concat(reload.Diagnostics).ToArray());
    }

    public OblivionStackOperationResult PopCard(
        OblivionWorkspaceSession current,
        string? pageId = null)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!TryGetNewlinePolicy(current, out OblivionVaultNewlinePolicy newlinePolicy, out IReadOnlyList<OblivionWorkspaceDiagnostic> configDiagnostics))
        {
            return new OblivionStackOperationResult(current, null, configDiagnostics);
        }

        string targetPageId = pageId ?? current.Workspace.DefaultPageId?.Value ?? string.Empty;
        string? selectedBefore = current.Workspace.Pages
            .FirstOrDefault(page => page.Id.Value == targetPageId) is { } pageBefore
                ? current.State.GetSelectedCardId(targetPageId, pageBefore.Cards)
                : null;
        OblivionStackMutationResult? mutation = OblivionStackMutation.Pop(
            current.Location.RootDirectory,
            pageId,
            newlinePolicy,
            out IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics);
        if (mutation is null)
        {
            return new OblivionStackOperationResult(current, null, diagnostics);
        }

        OblivionWorkspaceSessionReloadResult reload = ReloadWorkspace(current);
        OblivionWorkspaceSession next = reload.Session;
        if (string.Equals(selectedBefore, mutation.CardId, StringComparison.Ordinal))
        {
            OblivionWorkspacePage? pageAfter = next.Workspace.Pages.FirstOrDefault(
                page => page.Id.Value == mutation.PageId);
            string? newTop = pageAfter?.Cards.LastOrDefault()?.Id.Value;
            if (newTop is not null)
            {
                next = next with
                {
                    State = next.State.WithSelectedCard(mutation.PageId, newTop),
                };
            }
        }

        return new OblivionStackOperationResult(
            next,
            mutation,
            diagnostics.Concat(reload.Diagnostics).ToArray());
    }

    private bool TryGetNewlinePolicy(
        OblivionWorkspaceSession current,
        out OblivionVaultNewlinePolicy policy,
        out IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics)
    {
        OblivionConfigResult config = _configStore.Load();
        if (config.Config is null)
        {
            policy = default;
            diagnostics = config.Diagnostics.Select(diagnostic => OblivionWorkspaceValidator.Error(
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.Path)).ToArray();
            return false;
        }

        policy = config.Config.NewlinePolicy switch
        {
            OblivionNewlinePolicy.Preserve => OblivionVaultNewlinePolicy.Preserve,
            OblivionNewlinePolicy.Lf => OblivionVaultNewlinePolicy.Lf,
            OblivionNewlinePolicy.Crlf => OblivionVaultNewlinePolicy.Crlf,
            _ => throw new ArgumentOutOfRangeException(),
        };
        diagnostics = [];
        return true;
    }

    internal static OblivionWorkspaceLoadResult LoadVault(string vaultRoot)
    {
        try
        {
            OblivionWorkspaceLoadResult load = OblivionWorkspaceLoader.OpenVault(vaultRoot);
            if (!load.Succeeded || load.Workspace is null || load.Location is null)
            {
                return load;
            }

            List<OblivionWorkspaceDiagnostic> diagnostics = load.Diagnostics.ToList();
            OblivionTableCardRealizer realizer = new();
            foreach (OblivionCard card in load.Workspace.Pages.SelectMany(page => page.Cards))
            {
                if (card.Kind != OblivionCardKind.Table)
                {
                    continue;
                }

                OblivionTableCardRealization realization = realizer.Realize(
                    card,
                    load.Location.RootDirectory);
                diagnostics.AddRange(realization.Diagnostics.Select(diagnostic =>
                    new OblivionWorkspaceDiagnostic(
                        diagnostic.Severity,
                        diagnostic.Code,
                        diagnostic.Message,
                        diagnostic.SourcePath)));
            }

            return load with
            {
                Diagnostics = OblivionWorkspaceValidator.OrderDiagnostics(diagnostics),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            string fullRoot = Path.GetFullPath(vaultRoot);
            string manifestPath = OblivionStructuredVaultPaths.WorkspaceManifest(fullRoot);
            return new OblivionWorkspaceLoadResult(
                null,
                new OblivionWorkspaceLocation(fullRoot, manifestPath),
                [OblivionWorkspaceValidator.Error(
                    "workspace-unreadable",
                    $"Structured vault '{fullRoot}' could not be read: {exception.Message}",
                    manifestPath)]);
        }
    }

    private static OblivionSessionState ReconcileSession(
        OblivionSessionState current,
        OblivionWorkspace workspace)
    {
        Dictionary<string, double> mainOffsets = new(StringComparer.Ordinal);
        Dictionary<string, double> inspectorOffsets = new(StringComparer.Ordinal);
        Dictionary<string, string?> selections = new(StringComparer.Ordinal);
        Dictionary<string, double> sourceOffsets = new(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyDictionary<string, OblivionCardViewState>> cardStates =
            new(StringComparer.Ordinal);
        Dictionary<string, OblivionViewportState> viewportStates = new(StringComparer.Ordinal);
        Dictionary<string, OblivionDiagramViewportState> diagramViewportStates = new(StringComparer.Ordinal);

        foreach (OblivionWorkspacePage page in workspace.Pages)
        {
            string pageId = page.Id.Value;
            if (current.MainScrollOffsetByPageId.TryGetValue(pageId, out double mainOffset))
            {
                mainOffsets[pageId] = mainOffset;
            }

            if (current.InspectorScrollOffsetByPageId.TryGetValue(pageId, out double inspectorOffset))
            {
                inspectorOffsets[pageId] = inspectorOffset;
            }

            HashSet<string> validCardIds = page.Cards
                .Select(card => card.Id.Value)
                .ToHashSet(StringComparer.Ordinal);
            string? selectedCardId = current.SelectedCardByPageId.TryGetValue(
                pageId,
                out string? previousSelection) &&
                previousSelection is not null &&
                validCardIds.Contains(previousSelection)
                    ? previousSelection
                    : page.Cards.FirstOrDefault()?.Id.Value;
            selections[pageId] = selectedCardId;

            if (current.ViewportStateByPageId.TryGetValue(pageId, out OblivionViewportState? viewportState))
            {
                viewportStates[pageId] = viewportState;
            }

            if (current.CardViewStateByPageId.TryGetValue(
                pageId,
                out IReadOnlyDictionary<string, OblivionCardViewState>? previousStates))
            {
                cardStates[pageId] = previousStates
                    .Where(pair => validCardIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }

            foreach (string cardId in validCardIds)
            {
                if (current.RawSourceScrollOffsetByCardId.TryGetValue(cardId, out double sourceOffset))
                {
                    sourceOffsets[cardId] = sourceOffset;
                }

                if (current.DiagramViewportStateByCardId.TryGetValue(
                    cardId,
                    out OblivionDiagramViewportState? diagramViewportState))
                {
                    diagramViewportStates[cardId] = diagramViewportState;
                }
            }
        }

        return new OblivionSessionState(
            mainOffsets,
            inspectorOffsets,
            selections,
            sourceOffsets,
            cardStates,
            viewportStates,
            diagramViewportStates,
            new Dictionary<string, OblivionFunctionExecutionResult>(StringComparer.Ordinal),
            current.InspectorPaneSelected);
    }
}

public static class OblivionWorkspaceApplication
{
    public static OblivionWorkspaceLoadResult Load(
        string manifestPath,
        OblivionWorkspaceLoadOptions? options = null,
        bool useCache = true)
    {
        OblivionWorkspaceLoadResult result = OblivionWorkspaceLoader.Load(manifestPath, options, useCache);
        if (result.Workspace is null)
        {
            return result;
        }

        List<OblivionWorkspaceDiagnostic> diagnostics = result.Diagnostics.ToList();
        List<OblivionWorkspaceSection> sections = [];
        foreach (OblivionWorkspaceSection section in result.Workspace.Sections)
        {
            List<OblivionWorkspacePage> pages = [];
            foreach (OblivionWorkspacePage page in section.Pages)
            {
                if (!OblivionDocsDogfoodCatalog.IsDocsPage(section.Id, page.Id.Value))
                {
                    pages.Add(page);
                    continue;
                }

                DocsDogfoodPageData docs = OblivionDocsDogfoodCatalog.CreatePageData(manifestPath);
                diagnostics.AddRange(docs.Documents.SelectMany(document => document.Diagnostics));
                pages.Add(page with { Cards = [.. page.Cards, .. docs.Cards] });
            }

            sections.Add(section with { Pages = pages });
        }

        return result with
        {
            Workspace = result.Workspace with { Sections = sections },
            Diagnostics = OblivionWorkspaceValidator.OrderDiagnostics(diagnostics),
        };
    }
}
