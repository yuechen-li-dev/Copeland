namespace Oblivion.Product;

public sealed class OblivionCardHandlerRegistry
{
    private readonly IReadOnlyDictionary<OblivionCardKind, IOblivionCardHandler> _handlers;

    public OblivionCardHandlerRegistry(IEnumerable<IOblivionCardHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        Dictionary<OblivionCardKind, IOblivionCardHandler> map = new();

        foreach (IOblivionCardHandler handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            map[handler.Kind] = handler;
        }

        _handlers = map;
    }

    public IReadOnlyList<OblivionCardKind> RegisteredKinds =>
        _handlers.Keys.OrderBy(static kind => kind.ToString(), StringComparer.Ordinal).ToArray();

    public static OblivionCardHandlerRegistry CreateDefault()
    {
        return new OblivionCardHandlerRegistry(
        [
            new OblivionNoteCardHandler(),
            new OblivionStatusCardHandler(),
            new OblivionUiPreviewCardHandler(),
            new OblivionArtifactCardHandler(),
            new OblivionCodeFactCardHandler(),
            new OblivionCodeTheoryCardHandler(),
        ]);
    }

    public IOblivionCardHandler GetHandler(OblivionCardKind kind)
    {
        if (_handlers.TryGetValue(kind, out IOblivionCardHandler? handler))
        {
            return handler;
        }

        return new OblivionUnknownCardHandler(kind);
    }

    public OblivionBuiltCard BuildCard(
        OblivionCard card,
        string? pageId = null,
        string? workspaceId = null,
        OblivionEffectState? effectState = null,
        OblivionCardLocalState? localStateOverride = null)
    {
        ArgumentNullException.ThrowIfNull(card);

        OblivionEffectState effectiveEffectState = effectState ?? OblivionEffectState.Empty;
        OblivionCardContext cardContext = new(
            pageId ?? card.PageId?.Value,
            workspaceId ?? card.WorkspaceId?.Value,
            card.Body.SourceReference ?? card.Provenance.SourceReference,
            effectiveEffectState.GetLastRequest(card.Id),
            effectiveEffectState.GetLastResult(card.Id),
            localStateOverride);
        IOblivionCardHandler handler = GetHandler(card.Kind);
        OblivionCardRuntimeModel model = handler.BuildModel(card, cardContext);
        OblivionCompactCardView compactView = handler.BuildCompactView(model, new OblivionCardViewContext(model.LocalState));
        OblivionInspectorCardView inspectorView = handler.BuildInspectorView(model, new OblivionCardInspectorContext(model.LocalState));

        return new OblivionBuiltCard(card, model, compactView, inspectorView);
    }

    public OblivionEffectRequest? CreateEffectRequest(
        OblivionCard card,
        string pageId,
        string actionId,
        string? workspaceId = null,
        OblivionEffectState? effectState = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        OblivionBuiltCard builtCard = BuildCard(card, pageId, workspaceId, effectState);
        IOblivionCardHandler handler = GetHandler(card.Kind);
        OblivionCardActionInvocation invocation = new(
            card.Id,
            actionId,
            pageId,
            card.Body.SourceReference ?? card.Provenance.SourceReference);
        OblivionCardEffectContext context = new(
            pageId,
            workspaceId ?? card.WorkspaceId?.Value,
            card.Body.SourceReference ?? card.Provenance.SourceReference,
            builtCard.RuntimeModel.LocalState);
        return handler.CreateEffectRequest(
            builtCard.RuntimeModel,
            invocation,
            context);
    }
}

public abstract class OblivionCardHandlerBase : IOblivionCardHandler
{
    protected const string DeferredExecutionMessage = "Effect routing skeleton only.";
    protected const string ExecutionDeferredMessage = "Execution deferred to M13+.";

    public abstract OblivionCardKind Kind { get; }

    public OblivionCardRuntimeModel BuildModel(
        OblivionCard card,
        OblivionCardContext context)
    {
        ArgumentNullException.ThrowIfNull(card);

        OblivionCardLocalState localState = BuildLocalState(card, context);
        IReadOnlyList<OblivionCardDiagnostic> diagnostics = BuildDiagnostics(card, context);
        IReadOnlyList<OblivionCardArtifactRef> artifacts = BuildArtifacts(card, context);

        OblivionCardRuntimeModel seed = new(
            new OblivionCardIdentity(
                card.Id,
                card.Kind,
                context.PageId,
                context.WorkspaceId,
                context.SourcePath ?? card.Provenance.SourceReference),
            card.Status,
            localState,
            diagnostics,
            artifacts,
            [],
            context.LastEffectRequest,
            context.LastEffectResult,
            card,
            BuildKindModel(card, context));

        IReadOnlyList<OblivionCardActionDescriptor> actions = GetActions(seed, new OblivionCardActionContext(localState));

        return seed with
        {
            Actions = actions,
        };
    }

    public virtual IReadOnlyList<OblivionCardActionDescriptor> GetActions(
        OblivionCardRuntimeModel model,
        OblivionCardActionContext context)
    {
        return model.SourceCard.Actions
            .Select(action => new OblivionCardActionDescriptor(
                action.Id,
                action.Label,
                action.Enabled,
                BuildActionIntent(model.SourceCard, action),
                ActionRequiresEffect(model.SourceCard, action),
                action.Enabled ? OblivionCardActionAvailability.Enabled : OblivionCardActionAvailability.Disabled,
                ResolveEffectKind(model.SourceCard, action)))
            .ToArray();
    }

    public virtual OblivionEffectRequest? CreateEffectRequest(
        OblivionCardRuntimeModel model,
        OblivionCardActionInvocation invocation,
        OblivionCardEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(context);

        OblivionCardActionDescriptor? action = model.Actions.FirstOrDefault(candidate =>
            candidate.ActionId == invocation.ActionId);
        if (action is null || !action.RequiresEffect)
        {
            return null;
        }

        string requestId = BuildRequestId(invocation, action.EffectKind);
        OblivionEffectContext effectContext = BuildEffectContext(
            model,
            invocation,
            action,
            context);
        return action.EffectKind switch
        {
            OblivionCardEffectKind.RefreshMarkdown => new RefreshContentEffectRequest(requestId, invocation.CardId, effectContext),
            OblivionCardEffectKind.OpenSource => new OpenSourceEffectRequest(requestId, invocation.CardId, effectContext),
            OblivionCardEffectKind.CopySourcePath => new CopySourcePathEffectRequest(requestId, invocation.CardId, effectContext),
            OblivionCardEffectKind.OpenArtifact => new OpenArtifactEffectRequest(requestId, invocation.CardId, effectContext),
            OblivionCardEffectKind.RunCodeFact => new RunCodeFactEffectRequest(requestId, invocation.CardId, effectContext),
            OblivionCardEffectKind.RunCodeTheory => new RunCodeTheoryEffectRequest(requestId, invocation.CardId, effectContext),
            OblivionCardEffectKind.ExportCard => new ExportCardEffectRequest(requestId, invocation.CardId, effectContext),
            OblivionCardEffectKind.RenderPreview => new RenderPreviewEffectRequest(requestId, invocation.CardId, effectContext),
            OblivionCardEffectKind.None => new NoOpEffectRequest(requestId, invocation.CardId, effectContext),
            _ => new CustomEffectRequest(requestId, invocation.CardId, effectContext),
        };
    }

    public abstract OblivionCompactCardView BuildCompactView(
        OblivionCardRuntimeModel model,
        OblivionCardViewContext context);

    public virtual OblivionInspectorCardView BuildInspectorView(
        OblivionCardRuntimeModel model,
        OblivionCardInspectorContext context)
    {
        return new OblivionInspectorCardView(
            model.Identity.Id.Value,
            BuildStandardInspectorSections(model),
            PreferredHeight: 1760);
    }

    protected virtual object? BuildKindModel(
        OblivionCard card,
        OblivionCardContext context)
    {
        return null;
    }

    protected virtual OblivionCardLocalState BuildLocalState(
        OblivionCard card,
        OblivionCardContext context)
    {
        return context.LocalStateOverride ?? OblivionCardLocalState.CreateDefault(card.Id);
    }

    protected virtual IReadOnlyList<OblivionCardDiagnostic> BuildDiagnostics(
        OblivionCard card,
        OblivionCardContext context)
    {
        List<OblivionCardDiagnostic> diagnostics = [];

        diagnostics.AddRange(OblivionMarkdownBody.Project(card.Body).Diagnostics);

        return diagnostics;
    }

    protected virtual IReadOnlyList<OblivionCardArtifactRef> BuildArtifacts(
        OblivionCard card,
        OblivionCardContext context)
    {
        return card.Artifacts
            .Select(artifact => new OblivionCardArtifactRef(
                artifact.Id,
                artifact.Label,
                artifact.Kind,
                artifact.Reference,
                artifact.Generated))
            .ToArray();
    }

    protected virtual string BuildActionIntent(
        OblivionCard card,
        OblivionCardAction action)
    {
        return $"{card.Kind}:{action.Id}";
    }

    protected virtual bool ActionRequiresEffect(
        OblivionCard card,
        OblivionCardAction action)
    {
        return false;
    }

    protected virtual OblivionCardEffectKind ResolveEffectKind(
        OblivionCard card,
        OblivionCardAction action)
    {
        return OblivionCardEffectKind.None;
    }

    protected static IReadOnlyList<string> BuildActionBadgeLabels(OblivionCardRuntimeModel model)
    {
        return model.Actions
            .Select(action => $"{action.Label} {FormatActionBadgeSuffix(action.Availability)}")
            .ToArray();
    }

    protected static IReadOnlyList<string> BuildArtifactBadgeLabels(OblivionCardRuntimeModel model)
    {
        return model.Artifacts
            .Select(artifact => $"{artifact.Label} ({artifact.Kind})")
            .ToArray();
    }

    protected IReadOnlyList<string> BuildMetaBadges(
        OblivionCardRuntimeModel model,
        bool markdownBody)
    {
        List<string> badges =
        [
            OblivionCardLabels.KindLabel(model.Identity.Kind),
            OblivionCardLabels.StatusLabel(model.Status),
        ];

        OblivionContentPresentationPlan presentation = OblivionContentPresenterSelector.Select(
            model.SourceCard,
            new OblivionCardViewState(
                model.LocalState.IsExpanded,
                model.LocalState.BodyScrollOffset));
        badges.Add(presentation.ContentTypeLabel);

        if (model.Diagnostics.Count > 0)
        {
            badges.Add($"Diagnostics {model.Diagnostics.Count}");
        }

        return badges;
    }

    protected static string? BuildSourceLabel(OblivionCard card)
    {
        string? sourcePath = card.Body.SourceReference ?? card.Provenance.SourceReference;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        string fileName = Path.GetFileName(sourcePath);
        return string.IsNullOrWhiteSpace(fileName) ? sourcePath : fileName;
    }

    protected static string? BuildCollapsedSummaryLine(OblivionCard card)
    {
        return OblivionContentPresenterSelector.Select(
            card,
            OblivionCardViewState.Collapsed).CollapsedSummary;
    }

    protected IReadOnlyList<OblivionInspectorSectionView> BuildStandardInspectorSections(OblivionCardRuntimeModel model)
    {
        OblivionCard card = model.SourceCard;

        return
        [
            new OblivionInspectorSectionView(
                $"{card.Id.Value}.summary",
                card.Title,
                [],
                new OblivionInspectorTextBodyContent(
                [
                    $"Kind: {OblivionCardLabels.KindLabel(card.Kind)}",
                    $"Status: {OblivionCardLabels.StatusLabel(card.Status)}",
                    $"Body format: {OblivionCardLabels.BodyFormatLabel(card.Body.Format)}",
                    BuildInspectorSummaryLine(card),
                ]),
                Height: 188),
            new OblivionInspectorSectionView(
                $"{card.Id.Value}.metadata",
                "Metadata",
                [],
                new OblivionInspectorTextBodyContent(
                [
                    $"Card ID: {card.Id.Value}",
                    $"Page ID: {model.Identity.PageId ?? "<none>"}",
                    $"Source reference: {card.Provenance.SourceReference ?? "<none>"}",
                    $"Body source reference: {card.Body.SourceReference ?? "<inline>"}",
                    $"Workspace: {model.Identity.WorkspaceId ?? "<none>"}",
                    $"Tags: {FormatTags(card.Tags)}",
                    $"Local state expanded: {model.LocalState.IsExpanded.ToString().ToLowerInvariant()}",
                    $"Body scroll offset: {model.LocalState.BodyScrollOffset:0.###}",
                    $"Content presenter: {OblivionContentPresenterSelector.Select(card, new OblivionCardViewState(model.LocalState.IsExpanded, model.LocalState.BodyScrollOffset)).Items[0].PresenterKind}",
                    $"Rendered body surface: {(card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown ? "Expanded card body" : "Inspector body section")}",
                    $"Selected artifact: {model.LocalState.SelectedArtifactId ?? "<none>"}",
                ]),
                Height: 260),
            new OblivionInspectorSectionView(
                $"{card.Id.Value}.body",
                card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
                    ? "Raw Markdown Source"
                    : "Body",
                [],
                card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
                    ? new OblivionInspectorRawMarkdownSourceBodyContent(card.Body)
                    : new OblivionInspectorTextBodyContent(OblivionMarkdownBody.BuildInspectorLines(card.Body)),
                Height: 448,
                ClipContent: false),
            new OblivionInspectorSectionView(
                $"{card.Id.Value}.diagnostics",
                card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
                    ? "Markdown diagnostics"
                    : "Card diagnostics",
                [],
                new OblivionInspectorTextBodyContent(BuildDiagnosticLines(model.Diagnostics, card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown)),
                Height: 236),
            new OblivionInspectorSectionView(
                $"{card.Id.Value}.actions",
                "Available actions",
                [],
                new OblivionInspectorTextBodyContent(BuildActionLines(model.Actions)),
                Height: 236),
            new OblivionInspectorSectionView(
                $"{card.Id.Value}.artifacts",
                "Artifacts metadata",
                [],
                new OblivionInspectorTextBodyContent(BuildArtifactLines(model.Artifacts)),
                Height: 236),
            new OblivionInspectorSectionView(
                $"{card.Id.Value}.effects",
                "Effect routing",
                [],
                new OblivionInspectorTextBodyContent(BuildEffectLines(model)),
                Height: 284),
        ];
    }

    protected static string FormatTags(IReadOnlyList<string> tags)
    {
        return tags.Count == 0
            ? "<none>"
            : string.Join(", ", tags);
    }

    protected static string BuildRequestId(
        OblivionCardActionInvocation invocation,
        OblivionCardEffectKind effectKind)
    {
        return $"{invocation.PageId}:{invocation.CardId.Value}:{invocation.ActionId}:{effectKind}";
    }

    protected static OblivionEffectContext BuildEffectContext(
        OblivionCardRuntimeModel model,
        OblivionCardActionInvocation invocation,
        OblivionCardActionDescriptor action,
        OblivionCardEffectContext context)
    {
        return new OblivionEffectContext(
            invocation.ActionId,
            model.Identity.Kind,
            context.PageId,
            context.WorkspaceId,
            context.SourcePath,
            action.Intent);
    }

    private static string BuildInspectorSummaryLine(OblivionCard card)
    {
        return card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
            ? "The card owns Markdown model, preview, diagnostics, actions, and effect request creation while the shell owns routing and storage."
            : "The card owns its localized model, diagnostics, artifacts, actions, and effect request creation while the shell owns routing and storage.";
    }

    private static IReadOnlyList<string> BuildDiagnosticLines(
        IReadOnlyList<OblivionCardDiagnostic> diagnostics,
        bool markdownCard)
    {
        if (diagnostics.Count == 0)
        {
            return [markdownCard ? "No Markdown diagnostics." : "No card-local diagnostics."];
        }

        return diagnostics
            .Select(diagnostic =>
            {
                string location = diagnostic.Line is null || diagnostic.Column is null
                    ? string.Empty
                    : $" @ {diagnostic.Line}:{diagnostic.Column}";
                return $"{diagnostic.Severity} | {diagnostic.Code}{location} | {diagnostic.Message}";
            })
            .ToArray();
    }

    private static IReadOnlyList<string> BuildActionLines(IReadOnlyList<OblivionCardActionDescriptor> actions)
    {
        if (actions.Count == 0)
        {
            return ["No actions declared on this card.", DeferredExecutionMessage, ExecutionDeferredMessage];
        }

        return
        [
            DeferredExecutionMessage,
            ExecutionDeferredMessage,
            .. actions.Select(action => $"{action.Id} | {action.Label} | {FormatAvailability(action.Availability)} | intent {action.Intent} | effect {action.EffectKind}"),
        ];
    }

    private static IReadOnlyList<string> BuildArtifactLines(IReadOnlyList<OblivionCardArtifactRef> artifacts)
    {
        if (artifacts.Count == 0)
        {
            return ["No artifacts declared on this card."];
        }

        return artifacts
            .Select(artifact => $"{artifact.Id} | {artifact.Label} | {artifact.Kind} | path {artifact.Path ?? "<none>"} | generated {artifact.Generated.ToString().ToLowerInvariant()}")
            .ToArray();
    }

    private static IReadOnlyList<string> BuildEffectLines(OblivionCardRuntimeModel model)
    {
        if (model.LastEffectRequest is null || model.LastEffectResult is null)
        {
            return
            [
                DeferredExecutionMessage,
                ExecutionDeferredMessage,
                "No routed effect request has been recorded for this card yet.",
            ];
        }

        return
        [
            DeferredExecutionMessage,
            ExecutionDeferredMessage,
            $"Last request: {model.LastEffectRequest.RequestId} | {model.LastEffectRequest.Kind} | intent {model.LastEffectRequest.Intent}",
            $"Last result: {model.LastEffectResult.Kind} -> {model.LastEffectResult.Status} | {model.LastEffectResult.Message}",
            .. model.LastEffectResult.Diagnostics.Select(diagnostic =>
                $"Diagnostic: {diagnostic.Code} | {diagnostic.Severity} | {diagnostic.Message}"),
        ];
    }

    private static string FormatAvailability(OblivionCardActionAvailability availability)
    {
        return availability switch
        {
            OblivionCardActionAvailability.Enabled => "enabled metadata",
            OblivionCardActionAvailability.Disabled => "disabled metadata",
            OblivionCardActionAvailability.Deferred => "deferred routing",
            _ => "unknown",
        };
    }

    private static string FormatActionBadgeSuffix(OblivionCardActionAvailability availability)
    {
        return availability switch
        {
            OblivionCardActionAvailability.Enabled => "ready",
            OblivionCardActionAvailability.Disabled => "disabled",
            OblivionCardActionAvailability.Deferred => "deferred",
            _ => "unknown",
        };
    }

}

public sealed class OblivionNoteCardHandler : OblivionCardHandlerBase
{
    public override OblivionCardKind Kind => OblivionCardKind.Note;

    public override IReadOnlyList<OblivionCardActionDescriptor> GetActions(
        OblivionCardRuntimeModel model,
        OblivionCardActionContext context)
    {
        List<OblivionCardActionDescriptor> actions =
        [
            new(
                "refresh-markdown",
                "Refresh markdown",
                Enabled: true,
                Intent: "Note:refresh-markdown",
                RequiresEffect: true,
                Availability: OblivionCardActionAvailability.Enabled,
                EffectKind: OblivionCardEffectKind.RefreshMarkdown),
        ];

        if (!string.IsNullOrWhiteSpace(model.Identity.SourcePath))
        {
            actions.Add(new OblivionCardActionDescriptor(
                "open-source",
                "Open source",
                Enabled: true,
                Intent: "Note:open-source",
                RequiresEffect: true,
                Availability: OblivionCardActionAvailability.Enabled,
                EffectKind: OblivionCardEffectKind.OpenSource));
            actions.Add(new OblivionCardActionDescriptor(
                "copy-source-path",
                "Copy source path",
                Enabled: true,
                Intent: "Note:copy-source-path",
                RequiresEffect: true,
                Availability: OblivionCardActionAvailability.Enabled,
                EffectKind: OblivionCardEffectKind.CopySourcePath));
        }

        return actions;
    }

    protected override object? BuildKindModel(
        OblivionCard card,
        OblivionCardContext context)
    {
        return new OblivionMarkdownNoteKindModel(
            card.Body.RawText,
            OblivionMarkdownBody.Project(card.Body).Document is not null,
            OblivionMarkdownBody.Project(card.Body).Preview,
            OblivionMarkdownBody.Project(card.Body).Diagnostics.Count);
    }

    public override OblivionCompactCardView BuildCompactView(
        OblivionCardRuntimeModel model,
        OblivionCardViewContext context)
    {
        OblivionCard card = model.SourceCard;
        bool markdownBody = card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown;

        return new OblivionCompactCardView(
            card.Id.Value,
            card.Title,
            card.Subtitle,
            BuildSourceLabel(card),
            BuildCollapsedSummaryLine(card),
            BuildMetaBadges(model, markdownBody),
            card.Tags,
            markdownBody
                ? new OblivionCompactMarkdownBodyContent(card.Body)
                : new OblivionCompactPlainBodyContent(OblivionMarkdownBody.Project(card.Body).Preview),
            [],
            BuildArtifactBadgeLabels(model),
            model.LocalState.IsExpanded,
            model.LocalState.BodyScrollOffset,
            PreferredHeight: 204,
            ExpandedPreferredHeight: 452);
    }
}

public sealed record OblivionMarkdownNoteKindModel(
    string? RawText,
    bool HasDocumentMir,
    IReadOnlyList<string> PreviewLines,
    int DiagnosticCount);

public sealed class OblivionStatusCardHandler : OblivionCardHandlerBase
{
    public override OblivionCardKind Kind => OblivionCardKind.Status;

    protected override object? BuildKindModel(
        OblivionCard card,
        OblivionCardContext context)
    {
        return new OblivionStatusKindModel(OblivionMarkdownBody.Project(card.Body).Preview);
    }

    public override OblivionCompactCardView BuildCompactView(
        OblivionCardRuntimeModel model,
        OblivionCardViewContext context)
    {
        OblivionCard card = model.SourceCard;

        return new OblivionCompactCardView(
            card.Id.Value,
            card.Title,
            card.Subtitle,
            BuildSourceLabel(card),
            BuildCollapsedSummaryLine(card),
            BuildMetaBadges(model, markdownBody: false),
            card.Tags,
            new OblivionCompactPlainBodyContent(OblivionMarkdownBody.Project(card.Body).Preview),
            [],
            BuildArtifactBadgeLabels(model),
            model.LocalState.IsExpanded,
            model.LocalState.BodyScrollOffset,
            PreferredHeight: 204,
            ExpandedPreferredHeight: 204);
    }
}

public sealed record OblivionStatusKindModel(
    IReadOnlyList<string> Lines);

public sealed class OblivionUiPreviewCardHandler : OblivionCardHandlerBase
{
    public override OblivionCardKind Kind => OblivionCardKind.UiPreview;

    public override IReadOnlyList<OblivionCardActionDescriptor> GetActions(
        OblivionCardRuntimeModel model,
        OblivionCardActionContext context)
    {
        return
        [
            new OblivionCardActionDescriptor(
                "render-preview",
                "Render preview",
                Enabled: false,
                Intent: "UiPreview:render-preview",
                RequiresEffect: true,
                Availability: OblivionCardActionAvailability.Deferred,
                EffectKind: OblivionCardEffectKind.RenderPreview),
        ];
    }

    public override OblivionCompactCardView BuildCompactView(
        OblivionCardRuntimeModel model,
        OblivionCardViewContext context)
    {
        OblivionCard card = model.SourceCard;

        return new OblivionCompactCardView(
            card.Id.Value,
            card.Title,
            card.Subtitle,
            BuildSourceLabel(card),
            BuildCollapsedSummaryLine(card),
            BuildMetaBadges(model, markdownBody: false),
            card.Tags,
            new OblivionCompactPlainBodyContent(OblivionMarkdownBody.Project(card.Body).Preview),
            [],
            BuildArtifactBadgeLabels(model),
            model.LocalState.IsExpanded,
            model.LocalState.BodyScrollOffset,
            204,
            204);
    }
}

public sealed class OblivionArtifactCardHandler : OblivionCardHandlerBase
{
    public override OblivionCardKind Kind => OblivionCardKind.Artifact;

    public override IReadOnlyList<OblivionCardActionDescriptor> GetActions(
        OblivionCardRuntimeModel model,
        OblivionCardActionContext context)
    {
        return
        [
            new OblivionCardActionDescriptor(
                "open-artifact",
                "Open artifact",
                Enabled: true,
                Intent: "Artifact:open-artifact",
                RequiresEffect: true,
                Availability: OblivionCardActionAvailability.Enabled,
                EffectKind: OblivionCardEffectKind.OpenArtifact),
            new OblivionCardActionDescriptor(
                "export",
                "Export card",
                Enabled: false,
                Intent: "Artifact:export",
                RequiresEffect: true,
                Availability: OblivionCardActionAvailability.Deferred,
                EffectKind: OblivionCardEffectKind.ExportCard),
        ];
    }

    public override OblivionCompactCardView BuildCompactView(
        OblivionCardRuntimeModel model,
        OblivionCardViewContext context)
    {
        OblivionCard card = model.SourceCard;

        return new OblivionCompactCardView(
            card.Id.Value,
            card.Title,
            card.Subtitle,
            BuildSourceLabel(card),
            BuildCollapsedSummaryLine(card),
            BuildMetaBadges(model, markdownBody: false),
            card.Tags,
            new OblivionCompactPlainBodyContent(OblivionMarkdownBody.Project(card.Body).Preview),
            [],
            BuildArtifactBadgeLabels(model),
            model.LocalState.IsExpanded,
            model.LocalState.BodyScrollOffset,
            card.Artifacts.Count > 1 ? 212 : 204,
            420);
    }
}

public sealed class OblivionCodeFactCardHandler : OblivionCardHandlerBase
{
    public override OblivionCardKind Kind => OblivionCardKind.CodeFact;

    public override IReadOnlyList<OblivionCardActionDescriptor> GetActions(
        OblivionCardRuntimeModel model,
        OblivionCardActionContext context)
    {
        return
        [
            new OblivionCardActionDescriptor(
                "run",
                "Run fact",
                Enabled: false,
                Intent: "CodeFact:run",
                RequiresEffect: true,
                Availability: OblivionCardActionAvailability.Deferred,
                EffectKind: OblivionCardEffectKind.RunCodeFact),
            new OblivionCardActionDescriptor(
                "inspect-source",
                "Inspect source",
                Enabled: false,
                Intent: "CodeFact:inspect-source",
                RequiresEffect: true,
                Availability: OblivionCardActionAvailability.Deferred,
                EffectKind: OblivionCardEffectKind.OpenSource),
        ];
    }

    public override OblivionCompactCardView BuildCompactView(
        OblivionCardRuntimeModel model,
        OblivionCardViewContext context)
    {
        OblivionCard card = model.SourceCard;

        return new OblivionCompactCardView(
            card.Id.Value,
            card.Title,
            card.Subtitle,
            BuildSourceLabel(card),
            BuildCollapsedSummaryLine(card),
            BuildMetaBadges(model, markdownBody: false),
            card.Tags,
            new OblivionCompactPlainBodyContent(OblivionMarkdownBody.Project(card.Body).Preview),
            [],
            BuildArtifactBadgeLabels(model),
            model.LocalState.IsExpanded,
            model.LocalState.BodyScrollOffset,
            248,
            420);
    }
}

public sealed class OblivionCodeTheoryCardHandler : OblivionCardHandlerBase
{
    public override OblivionCardKind Kind => OblivionCardKind.CodeTheory;

    public override IReadOnlyList<OblivionCardActionDescriptor> GetActions(
        OblivionCardRuntimeModel model,
        OblivionCardActionContext context)
    {
        return
        [
            new OblivionCardActionDescriptor(
                "run-theory",
                "Run theory",
                Enabled: false,
                Intent: "CodeTheory:run-theory",
                RequiresEffect: true,
                Availability: OblivionCardActionAvailability.Deferred,
                EffectKind: OblivionCardEffectKind.RunCodeTheory),
            new OblivionCardActionDescriptor(
                "inspect-source",
                "Inspect source",
                Enabled: false,
                Intent: "CodeTheory:inspect-source",
                RequiresEffect: true,
                Availability: OblivionCardActionAvailability.Deferred,
                EffectKind: OblivionCardEffectKind.OpenSource),
        ];
    }

    public override OblivionCompactCardView BuildCompactView(
        OblivionCardRuntimeModel model,
        OblivionCardViewContext context)
    {
        OblivionCard card = model.SourceCard;

        return new OblivionCompactCardView(
            card.Id.Value,
            card.Title,
            card.Subtitle,
            BuildSourceLabel(card),
            BuildCollapsedSummaryLine(card),
            BuildMetaBadges(model, markdownBody: false),
            card.Tags,
            new OblivionCompactPlainBodyContent(OblivionMarkdownBody.Project(card.Body).Preview),
            [],
            BuildArtifactBadgeLabels(model),
            model.LocalState.IsExpanded,
            model.LocalState.BodyScrollOffset,
            312,
            420);
    }
}

public sealed record OblivionEffectOutcome(
    OblivionEffectRequest Request,
    OblivionEffectResult Result);

public sealed class OblivionUnknownCardHandler : OblivionCardHandlerBase
{
    private readonly OblivionCardKind _requestedKind;

    public OblivionUnknownCardHandler(OblivionCardKind requestedKind)
    {
        _requestedKind = requestedKind;
    }

    public override OblivionCardKind Kind => _requestedKind;

    protected override IReadOnlyList<OblivionCardDiagnostic> BuildDiagnostics(
        OblivionCard card,
        OblivionCardContext context)
    {
        List<OblivionCardDiagnostic> diagnostics = base.BuildDiagnostics(card, context).ToList();
        diagnostics.Add(
            new OblivionCardDiagnostic(
                "M12E-UNKNOWN-KIND",
                OblivionDiagnosticSeverity.Error,
                $"No handler was registered for card kind '{_requestedKind}'.",
                card.Provenance.SourceReference));
        return diagnostics;
    }

    public override OblivionCompactCardView BuildCompactView(
        OblivionCardRuntimeModel model,
        OblivionCardViewContext context)
    {
        return new OblivionCompactCardView(
            model.Identity.Id.Value,
            model.SourceCard.Title,
            model.SourceCard.Subtitle,
            BuildSourceLabel(model.SourceCard),
            BuildCollapsedSummaryLine(model.SourceCard),
            BuildMetaBadges(model, markdownBody: false),
            model.SourceCard.Tags,
            new OblivionCompactPlainBodyContent(
            [
                "Missing card handler.",
                $"Kind: {_requestedKind}",
                "Rendering stayed bounded.",
            ]),
            [],
            BuildArtifactBadgeLabels(model),
            model.LocalState.IsExpanded,
            model.LocalState.BodyScrollOffset,
            PreferredHeight: 204,
            ExpandedPreferredHeight: 204);
    }
}
