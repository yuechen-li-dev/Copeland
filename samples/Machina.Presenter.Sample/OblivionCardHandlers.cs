namespace Machina.Presenter.Sample;

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
            new OblivionDeferredPlaceholderCardHandler(
                OblivionCardKind.UiPreview,
                "UI preview rendering remains localized placeholder behavior in M12e."),
            new OblivionDeferredPlaceholderCardHandler(
                OblivionCardKind.Artifact,
                "Artifact cards remain metadata-only in M12e."),
            new OblivionDeferredPlaceholderCardHandler(
                OblivionCardKind.CodeFact,
                "CodeFact execution is deferred until a future Dominatus-routed milestone.",
                actionsRequireEffect: true),
            new OblivionDeferredPlaceholderCardHandler(
                OblivionCardKind.CodeTheory,
                "CodeTheory execution is deferred until a future Dominatus-routed milestone.",
                actionsRequireEffect: true),
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
        string? workspaceId = null)
    {
        ArgumentNullException.ThrowIfNull(card);

        OblivionCardContext cardContext = new(
            pageId ?? card.PageId,
            workspaceId ?? card.WorkspaceId,
            card.SourcePath);
        IOblivionCardHandler handler = GetHandler(card.Kind);
        OblivionCardRuntimeModel model = handler.BuildModel(card, cardContext);
        OblivionCompactCardView compactView = handler.BuildCompactView(model, new OblivionCardViewContext(model.LocalState));
        OblivionInspectorCardView inspectorView = handler.BuildInspectorView(model, new OblivionCardInspectorContext(model.LocalState));

        return new OblivionBuiltCard(card, model, compactView, inspectorView);
    }
}

public abstract class OblivionCardHandlerBase : IOblivionCardHandler
{
    protected const string DeferredExecutionMessage = "Actions and effect requests remain deferred metadata only in M12e.";

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
                context.SourcePath ?? card.SourcePath),
            card.Status,
            localState,
            diagnostics,
            artifacts,
            [],
            [],
            card,
            BuildKindModel(card, context));

        IReadOnlyList<OblivionCardActionDescriptor> actions = GetActions(seed, new OblivionCardActionContext(localState));
        IReadOnlyList<OblivionCardEffectRequest> effectRequests = BuildEffectRequests(actions);

        return seed with
        {
            Actions = actions,
            EffectRequests = effectRequests,
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
                ActionRequiresEffect(model.SourceCard, action)))
            .ToArray();
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
        return OblivionCardLocalState.CreateDefault(card.Id);
    }

    protected virtual IReadOnlyList<OblivionCardDiagnostic> BuildDiagnostics(
        OblivionCard card,
        OblivionCardContext context)
    {
        List<OblivionCardDiagnostic> diagnostics = [];

        foreach (OblivionWorkspaceDiagnostic diagnostic in card.Body.Diagnostics)
        {
            diagnostics.Add(
                new OblivionCardDiagnostic(
                    diagnostic.Code,
                    MapSeverity(diagnostic.Severity),
                    diagnostic.Message,
                    diagnostic.SourcePath,
                    diagnostic.Line,
                    diagnostic.Column));
        }

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
                artifact.Path,
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

    protected static IReadOnlyList<string> BuildActionBadgeLabels(OblivionCardRuntimeModel model)
    {
        return model.Actions
            .Select(action => $"{action.Label} {(action.Enabled ? "ready" : "disabled")}")
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

        if (markdownBody)
        {
            badges.Add("Markdown body");
        }

        if (model.Diagnostics.Count > 0)
        {
            badges.Add($"Diagnostics {model.Diagnostics.Count}");
        }

        return badges;
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
                    BuildSummaryLine(card),
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
                    $"Source path: {card.SourcePath ?? "<none>"}",
                    $"Body source path: {card.Body.BodySourcePath ?? "<inline>"}",
                    $"Workspace: {model.Identity.WorkspaceId ?? "<none>"}",
                    $"Tags: {FormatTags(card.Tags)}",
                    $"Local state expanded: {model.LocalState.IsExpanded.ToString().ToLowerInvariant()}",
                    $"Selected artifact: {model.LocalState.SelectedArtifactId ?? "<none>"}",
                ]),
                Height: 260),
            new OblivionInspectorSectionView(
                $"{card.Id.Value}.body",
                "Body",
                card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
                    ? ["DocumentMir rendered", "Static Markdown"]
                    : [],
                card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
                    ? new OblivionInspectorMarkdownBodyContent(card.Body)
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
                "Actions metadata",
                [],
                new OblivionInspectorTextBodyContent(BuildActionLines(model.Actions)),
                Height: 212),
            new OblivionInspectorSectionView(
                $"{card.Id.Value}.artifacts",
                "Artifacts metadata",
                [],
                new OblivionInspectorTextBodyContent(BuildArtifactLines(model.Artifacts)),
                Height: 236),
            new OblivionInspectorSectionView(
                $"{card.Id.Value}.effects",
                "Execution result",
                [],
                new OblivionInspectorTextBodyContent(BuildEffectLines(model)),
                Height: 212),
        ];
    }

    protected static IReadOnlyList<OblivionCardEffectRequest> BuildEffectRequests(
        IReadOnlyList<OblivionCardActionDescriptor> actions)
    {
        return actions
            .Where(action => action.RequiresEffect)
            .Select(action => new OblivionCardEffectRequest(
                $"{action.Id}.effect",
                "deferred",
                action.Intent,
                Deferred: true))
            .ToArray();
    }

    protected static string FormatTags(IReadOnlyList<string> tags)
    {
        return tags.Count == 0
            ? "<none>"
            : string.Join(", ", tags);
    }

    private static string BuildSummaryLine(OblivionCard card)
    {
        return card.Body.Format == OblivionCardBodyFormat.CopelandMarkdown
            ? "The card owns Markdown model, preview, diagnostics, and inspector rendering while the shell stays responsible for navigation and layout."
            : "The card owns its localized model, diagnostics, artifacts, actions, and inspector rendering while the shell stays responsible for navigation and layout.";
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
            return ["No actions declared on this card.", DeferredExecutionMessage];
        }

        return
        [
            DeferredExecutionMessage,
            .. actions.Select(action => $"{action.Id} | {action.Label} | {(action.Enabled ? "enabled metadata" : "disabled metadata")} | intent {action.Intent} | effect {action.RequiresEffect.ToString().ToLowerInvariant()}"),
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
        if (model.EffectRequests.Count == 0)
        {
            return
            [
                "Not executed in M11g.",
                "No future effect requests declared on this card.",
                DeferredExecutionMessage,
            ];
        }

        return
        [
            "Not executed in M11g.",
            "Effect requests are declared but not executable in M12e.",
            .. model.EffectRequests.Select(effect => $"{effect.Id} | {effect.Kind} | intent {effect.Intent} | deferred {effect.Deferred.ToString().ToLowerInvariant()}"),
        ];
    }

    private static OblivionCardDiagnosticSeverity MapSeverity(OblivionWorkspaceDiagnosticSeverity severity)
    {
        return severity switch
        {
            OblivionWorkspaceDiagnosticSeverity.Error => OblivionCardDiagnosticSeverity.Error,
            OblivionWorkspaceDiagnosticSeverity.Warning => OblivionCardDiagnosticSeverity.Warning,
            _ => OblivionCardDiagnosticSeverity.Info,
        };
    }
}

public sealed class OblivionNoteCardHandler : OblivionCardHandlerBase
{
    public override OblivionCardKind Kind => OblivionCardKind.Note;

    protected override object? BuildKindModel(
        OblivionCard card,
        OblivionCardContext context)
    {
        return new OblivionMarkdownNoteKindModel(
            card.Body.RawText,
            card.Body.DocumentMir is not null,
            card.Body.PreviewLines,
            card.Body.Diagnostics.Count);
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
            BuildMetaBadges(model, markdownBody),
            card.Tags,
            markdownBody
                ? new OblivionCompactMarkdownBodyContent(card.Body)
                : new OblivionCompactPlainBodyContent(card.BodyLines),
            BuildActionBadgeLabels(model),
            BuildArtifactBadgeLabels(model),
            PreferredHeight: 168);
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
        return new OblivionStatusKindModel(card.BodyLines);
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
            BuildMetaBadges(model, markdownBody: false),
            card.Tags,
            new OblivionCompactPlainBodyContent(card.BodyLines),
            BuildActionBadgeLabels(model),
            BuildArtifactBadgeLabels(model),
            PreferredHeight: 168);
    }
}

public sealed record OblivionStatusKindModel(
    IReadOnlyList<string> Lines);

public sealed class OblivionDeferredPlaceholderCardHandler : OblivionCardHandlerBase
{
    private readonly string _deferredMessage;
    private readonly bool _actionsRequireEffect;

    public OblivionDeferredPlaceholderCardHandler(
        OblivionCardKind kind,
        string deferredMessage,
        bool actionsRequireEffect = false)
    {
        Kind = kind;
        _deferredMessage = deferredMessage;
        _actionsRequireEffect = actionsRequireEffect;
    }

    public override OblivionCardKind Kind { get; }

    protected override object? BuildKindModel(
        OblivionCard card,
        OblivionCardContext context)
    {
        return new OblivionDeferredPlaceholderKindModel(
            _deferredMessage,
            _actionsRequireEffect);
    }

    protected override bool ActionRequiresEffect(
        OblivionCard card,
        OblivionCardAction action)
    {
        return _actionsRequireEffect;
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
            BuildMetaBadges(model, markdownBody: false),
            card.Tags,
            new OblivionCompactPlainBodyContent(card.BodyLines),
            BuildActionBadgeLabels(model),
            BuildArtifactBadgeLabels(model),
            DeterminePreferredHeight(card));
    }

    public override OblivionInspectorCardView BuildInspectorView(
        OblivionCardRuntimeModel model,
        OblivionCardInspectorContext context)
    {
        IReadOnlyList<OblivionInspectorSectionView> sections = BuildStandardInspectorSections(model);
        sections = sections
            .Select(section => section.Id.EndsWith(".effects", StringComparison.Ordinal)
                ? section with
                {
                    Body = new OblivionInspectorTextBodyContent(
                    [
                        _deferredMessage,
                        .. ((OblivionInspectorTextBodyContent)section.Body).Lines,
                    ]),
                }
                : section)
            .ToArray();

        return new OblivionInspectorCardView(model.Identity.Id.Value, sections, 1760);
    }

    private static double DeterminePreferredHeight(OblivionCard card)
    {
        return card.Kind switch
        {
            OblivionCardKind.CodeFact => 248,
            OblivionCardKind.CodeTheory => 312,
            OblivionCardKind.UiPreview => 184,
            OblivionCardKind.Artifact when card.Artifacts.Count > 1 => 196,
            _ => 168,
        };
    }
}

public sealed record OblivionDeferredPlaceholderKindModel(
    string DeferredMessage,
    bool ActionsRequireEffect);

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
                OblivionCardDiagnosticSeverity.Error,
                $"No handler was registered for card kind '{_requestedKind}'.",
                card.SourcePath));
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
            BuildMetaBadges(model, markdownBody: false),
            model.SourceCard.Tags,
            new OblivionCompactPlainBodyContent(
            [
                "Missing card handler.",
                $"Kind: {_requestedKind}",
                "Rendering stayed bounded.",
            ]),
            BuildActionBadgeLabels(model),
            BuildArtifactBadgeLabels(model),
            PreferredHeight: 168);
    }
}
