using Oblivion.Model;
using Oblivion.Persistence;
using Oblivion.Product;

namespace Oblivion.App;

public enum OblivionCommandId
{
    WorkspaceReload,
    CardsExpandAll,
    CardsCollapseAll,
    LayoutSingle,
    LayoutVerticalSplit,
    LayoutHorizontalSplit,
    LayoutFocusNext,
    DiagramFit,
    DiagramZoomIn,
    DiagramZoomOut,
    DiagramResetView,
    FunctionRun,
}

public enum OblivionCommandScope
{
    Workspace,
    ActivePage,
    FocusedCard,
}

public sealed record OblivionCommandDescriptor(
    OblivionCommandId CommandId,
    string Id,
    string Title,
    string Description,
    OblivionCommandScope Scope,
    bool Available);

public sealed record OblivionCommandExecutionResult(
    OblivionCommandDescriptor? Command,
    OblivionWorkspaceSession Session,
    bool Executed,
    int AffectedCards,
    IReadOnlyList<OblivionWorkspaceDiagnostic> Diagnostics)
{
    public bool Succeeded =>
        Executed &&
        Diagnostics.All(diagnostic => diagnostic.Severity != OblivionDiagnosticSeverity.Error);
}

public sealed class OblivionCommandRegistry
{
    private static readonly IReadOnlyList<OblivionCommandDescriptor> Registered =
    [
        new(
            OblivionCommandId.WorkspaceReload,
            "workspace.reload",
            "Reload workspace",
            "Transactionally reload the process-local workspace session.",
            OblivionCommandScope.Workspace,
            Available: true),
        new(
            OblivionCommandId.CardsExpandAll,
            "cards.expand-all",
            "Expand all cards",
            "Expand every Card on the active Page in process-local session state.",
            OblivionCommandScope.ActivePage,
            Available: true),
        new(
            OblivionCommandId.CardsCollapseAll,
            "cards.collapse-all",
            "Collapse all cards",
            "Collapse every Card on the active Page in process-local session state.",
            OblivionCommandScope.ActivePage,
            Available: true),
        new(
            OblivionCommandId.LayoutSingle,
            "layout.single",
            "Single viewport slot",
            "Project the selected Card into one viewport slot.",
            OblivionCommandScope.ActivePage,
            Available: true),
        new(
            OblivionCommandId.LayoutVerticalSplit,
            "layout.vertical-split",
            "Vertical viewport split",
            "Project the selected and next Cards into top and bottom viewport slots.",
            OblivionCommandScope.ActivePage,
            Available: true),
        new(
            OblivionCommandId.LayoutHorizontalSplit,
            "layout.horizontal-split",
            "Horizontal viewport split",
            "Project the selected and next Cards into left and right viewport slots.",
            OblivionCommandScope.ActivePage,
            Available: true),
        new(
            OblivionCommandId.LayoutFocusNext,
            "layout.focus-next",
            "Focus next viewport slot",
            "Move process-local focus between explicit viewport slots.",
            OblivionCommandScope.ActivePage,
            Available: true),
        new(
            OblivionCommandId.DiagramFit,
            "diagram.fit",
            "Fit diagram",
            "Fit the Diagram Card in the focused viewport slot.",
            OblivionCommandScope.FocusedCard,
            Available: true),
        new(
            OblivionCommandId.DiagramZoomIn,
            "diagram.zoom-in",
            "Zoom diagram in",
            "Increase the focused Diagram Card camera zoom within its bound.",
            OblivionCommandScope.FocusedCard,
            Available: true),
        new(
            OblivionCommandId.DiagramZoomOut,
            "diagram.zoom-out",
            "Zoom diagram out",
            "Decrease the focused Diagram Card camera zoom within its bound.",
            OblivionCommandScope.FocusedCard,
            Available: true),
        new(
            OblivionCommandId.DiagramResetView,
            "diagram.reset-view",
            "Reset diagram view",
            "Reset the focused Diagram Card camera to Fit.",
            OblivionCommandScope.FocusedCard,
            Available: true),
        new(
            OblivionCommandId.FunctionRun,
            "function.run",
            "Run Function",
            "Run the exact xUnit test owned by the focused Function Card.",
            OblivionCommandScope.FocusedCard,
            Available: true),
    ];

    public IReadOnlyList<OblivionCommandDescriptor> Descriptors => Registered;

    public bool TryResolve(string externalId, out OblivionCommandId commandId)
    {
        OblivionCommandDescriptor? descriptor = Registered.FirstOrDefault(
            candidate => string.Equals(candidate.Id, externalId, StringComparison.Ordinal));
        commandId = descriptor?.CommandId ?? default;
        return descriptor is not null;
    }

    public OblivionCommandExecutionResult Run(
        OblivionApplication application,
        OblivionWorkspaceSession session,
        OblivionCommandId commandId)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(session);

        OblivionCommandDescriptor descriptor = Registered.Single(
            candidate => candidate.CommandId == commandId);
        return commandId switch
        {
            OblivionCommandId.WorkspaceReload => Reload(application, session, descriptor),
            OblivionCommandId.CardsExpandAll => SetExpansion(session, descriptor, expanded: true),
            OblivionCommandId.CardsCollapseAll => SetExpansion(session, descriptor, expanded: false),
            OblivionCommandId.LayoutSingle => SetLayout(
                session,
                descriptor,
                OblivionViewportLayoutMode.Single),
            OblivionCommandId.LayoutVerticalSplit => SetLayout(
                session,
                descriptor,
                OblivionViewportLayoutMode.VerticalSplit),
            OblivionCommandId.LayoutHorizontalSplit => SetLayout(
                session,
                descriptor,
                OblivionViewportLayoutMode.HorizontalSplit),
            OblivionCommandId.LayoutFocusNext => FocusNext(session, descriptor),
            OblivionCommandId.DiagramFit => SetDiagramView(
                session,
                descriptor,
                view => view.Reset()),
            OblivionCommandId.DiagramZoomIn => SetDiagramView(
                session,
                descriptor,
                view => view.ZoomBy(OblivionDiagramViewportState.ZoomStep)),
            OblivionCommandId.DiagramZoomOut => SetDiagramView(
                session,
                descriptor,
                view => view.ZoomBy(1 / OblivionDiagramViewportState.ZoomStep)),
            OblivionCommandId.DiagramResetView => SetDiagramView(
                session,
                descriptor,
                view => view.Reset()),
            OblivionCommandId.FunctionRun => RunFunction(application, session, descriptor),
            _ => throw new ArgumentOutOfRangeException(nameof(commandId)),
        };
    }

    private static OblivionCommandExecutionResult Reload(
        OblivionApplication application,
        OblivionWorkspaceSession session,
        OblivionCommandDescriptor descriptor)
    {
        OblivionWorkspaceSessionReloadResult reload = application.ReloadWorkspace(session);
        return new(
            descriptor,
            reload.Session,
            reload.Reloaded,
            AffectedCards: 0,
            reload.Diagnostics);
    }

    private static OblivionCommandExecutionResult SetExpansion(
        OblivionWorkspaceSession session,
        OblivionCommandDescriptor descriptor,
        bool expanded)
    {
        string pageId = session.ActivePage.Id.Value;
        OblivionSessionState state = session.State;
        int affected = 0;
        foreach (OblivionCard card in session.ActivePage.Cards)
        {
            OblivionCardViewState current = state.GetCardViewState(pageId, card.Id.Value);
            if (current.IsExpanded != expanded)
            {
                state = state.WithCardViewState(pageId, card.Id.Value, current with { IsExpanded = expanded });
                affected++;
            }
        }

        return new(
            descriptor,
            session with { State = state },
            Executed: true,
            affected,
            []);
    }

    private static OblivionCommandExecutionResult SetLayout(
        OblivionWorkspaceSession session,
        OblivionCommandDescriptor descriptor,
        OblivionViewportLayoutMode mode)
    {
        string pageId = session.ActivePage.Id.Value;
        OblivionViewportState viewport = session.State.GetViewportState(pageId).WithLayout(mode);
        return Success(
            descriptor,
            session with { State = session.State.WithViewportState(pageId, viewport) });
    }

    private static OblivionCommandExecutionResult FocusNext(
        OblivionWorkspaceSession session,
        OblivionCommandDescriptor descriptor)
    {
        string pageId = session.ActivePage.Id.Value;
        OblivionViewportState viewport = session.State.GetViewportState(pageId).FocusNext();
        return Success(
            descriptor,
            session with { State = session.State.WithViewportState(pageId, viewport) });
    }

    private static OblivionCommandExecutionResult SetDiagramView(
        OblivionWorkspaceSession session,
        OblivionCommandDescriptor descriptor,
        Func<OblivionDiagramViewportState, OblivionDiagramViewportState> update)
    {
        string pageId = session.ActivePage.Id.Value;
        OblivionViewportState viewport = session.State.GetViewportState(pageId);
        string? selectedCardId = session.State.GetSelectedCardId(pageId, session.ActivePage.Cards);
        string? cardId = OblivionViewportAssignments.ResolveFocusedCardId(
            viewport,
            session.ActivePage.Cards,
            selectedCardId);
        OblivionCard? card = session.ActivePage.Cards.FirstOrDefault(
            candidate => string.Equals(candidate.Id.Value, cardId, StringComparison.Ordinal));
        if (card?.Kind != OblivionCardKind.Diagram)
        {
            return new OblivionCommandExecutionResult(
                descriptor,
                session,
                Executed: false,
                AffectedCards: 0,
                [OblivionWorkspaceValidator.Error(
                    "OBLIVION-DIAGRAM-FOCUSED-CARD-REQUIRED",
                    "The focused viewport slot does not contain a Diagram Card.",
                    session.Location.ManifestPath)]);
        }

        OblivionDiagramViewportState current = session.State.GetDiagramViewportState(card.Id.Value);
        OblivionSessionState state = session.State.WithDiagramViewportState(
            card.Id.Value,
            update(current));
        return Success(descriptor, session with { State = state }, affectedCards: 1);
    }

    private static OblivionCommandExecutionResult Success(
        OblivionCommandDescriptor descriptor,
        OblivionWorkspaceSession session,
        int affectedCards = 0)
    {
        return new OblivionCommandExecutionResult(
            descriptor,
            session,
            Executed: true,
            affectedCards,
            []);
    }

    private static OblivionCommandExecutionResult RunFunction(
        OblivionApplication application,
        OblivionWorkspaceSession session,
        OblivionCommandDescriptor descriptor)
    {
        string pageId = session.ActivePage.Id.Value;
        string? selectedCardId = session.State.GetSelectedCardId(pageId, session.ActivePage.Cards);
        string? cardId = OblivionViewportAssignments.ResolveFocusedCardId(
            session.State.GetViewportState(pageId),
            session.ActivePage.Cards,
            selectedCardId);
        OblivionCard? card = session.ActivePage.Cards.FirstOrDefault(candidate =>
            candidate.Id.Value == cardId);
        if (card?.Kind != OblivionCardKind.Function)
        {
            return new OblivionCommandExecutionResult(
                descriptor,
                session,
                Executed: false,
                AffectedCards: 0,
                [OblivionWorkspaceValidator.Error(
                    "OBLIVION-FUNCTION-FOCUS-REQUIRED",
                    "The focused viewport slot does not contain a Function Card.",
                    session.Location.ManifestPath)]);
        }

        OblivionFunctionRunResult run = application.RunFunctionCard(session, card.Id.Value);
        IReadOnlyList<OblivionWorkspaceDiagnostic> diagnostics = run.Result.Diagnostics
            .Select(diagnostic => new OblivionWorkspaceDiagnostic(
                diagnostic.Severity,
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.SourcePath,
                diagnostic.DisplaySeverity,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.SpanStart,
                diagnostic.SpanLength))
            .ToArray();
        return new OblivionCommandExecutionResult(
            descriptor,
            run.Session,
            Executed: run.Result.Outcome != OblivionFunctionExecutionOutcome.Error,
            AffectedCards: 1,
            diagnostics);
    }
}
