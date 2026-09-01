using Machina.Core.Actions;
using Machina.Layout.Geometry;
using Oblivion.App;
using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.Standalone;

public sealed class OblivionStandaloneSurface
{
    private readonly OblivionCardHandlerRegistry _cardHandlers;
    private readonly OblivionStandaloneStyle _style;
    private readonly string _workspaceRoot;
    private readonly OblivionDiagramCardRealizer _diagramRealizer = new();
    private readonly OblivionApplication _application;
    private readonly OblivionCommandRegistry _commands = new();
    private OblivionWorkspaceSession _workspaceSession;

    public OblivionStandaloneSurface(
        string? vaultRoot = null,
        OblivionStandaloneStyle? style = null)
    {
        _style = style ?? OblivionStandaloneStyles.Dark;
        _application = new OblivionApplication();
        OblivionWorkspaceSessionOpenResult open = _application.OpenWorkspace(
            vaultRoot ?? M19iStructuredVault.DefaultRoot);
        if (!open.Succeeded || open.Session is null)
        {
            throw new InvalidOperationException(
                "The standalone structured vault could not be opened:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, open.Diagnostics));
        }

        _workspaceSession = open.Session;
        Workspace = _workspaceSession.Workspace;
        Page = _workspaceSession.ActivePage;
        Cards = ValidateCards(Page);
        _workspaceRoot = open.Session.Location.RootDirectory;
        _cardHandlers = OblivionCardHandlerRegistry.CreateDefault();
    }

    public OblivionWorkspace Workspace { get; }

    public OblivionWorkspacePage Page { get; }

    public IReadOnlyList<OblivionCard> Cards { get; }

    public string PageId => Page.Id.Value;

    public OblivionSessionState Session => _workspaceSession.State;

    public OblivionViewportState Viewport => Session.GetViewportState(PageId);

    public string? SelectedCardId => Session.GetSelectedCardId(PageId, Cards);

    public bool AreAllCardsExpanded => Cards.All(IsExpanded);

    public bool IsExpanded(OblivionCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return Session.GetCardViewState(PageId, card.Id.Value).IsExpanded;
    }

    public OblivionStandaloneSurfaceSnapshot CreateSnapshot(int width, int height)
    {
        IReadOnlyList<OblivionViewportAssignment> assignments = OblivionViewportAssignments.Resolve(
            Viewport,
            Cards,
            SelectedCardId);
        List<OblivionStandaloneCardPresentation> cardPresentations = [];
        foreach (OblivionViewportAssignment assignment in assignments)
        {
            OblivionCard? card = Cards.FirstOrDefault(candidate =>
                string.Equals(candidate.Id.Value, assignment.CardId, StringComparison.Ordinal));
            if (card is null)
            {
                continue;
            }

            OblivionCardViewState state = Session.GetCardViewState(PageId, card.Id.Value);
            OblivionCardLocalState localState = OblivionCardLocalState.CreateDefault(card.Id) with
            {
                IsExpanded = state.IsExpanded,
                BodyScrollOffset = state.BodyScrollOffset,
            };
            OblivionBuiltCard built = _cardHandlers.BuildCard(
                card,
                PageId,
                Workspace.Id.Value,
                localStateOverride: localState);
            OblivionCompactCardView cardView = built.CompactView with
            {
                Subtitle = _style.CardSubtitle,
                SourceLabel = null,
                SummaryLine = null,
                MetaBadges = state.IsExpanded
                    ? [CardTypeLabel(card), "Expanded"]
                    : [CardTypeLabel(card), "Collapsed"],
                Tags = [],
                ActionBadges = [],
                ArtifactBadges = [],
            };
            OblivionContentPresentationPlan contentPlan = OblivionContentPresenterSelector.Select(
                card,
                state,
                diagram: CreateDiagramPresentationSource(card),
                table: CreateTablePresentationSource(card));
            bool isSelected = string.Equals(
                SelectedCardId,
                card.Id.Value,
                StringComparison.Ordinal);
            cardPresentations.Add(new OblivionStandaloneCardPresentation(
                card,
                cardView,
                contentPlan,
                isSelected,
                assignment.SlotId,
                Session.GetDiagramViewportState(card.Id.Value)));
        }

        return OblivionStandaloneRenderer.Render(
            width,
            height,
            Viewport,
            assignments,
            cardPresentations,
            _style);
    }

    public void ToggleExpansion(string cardId)
    {
        Dispatch(OblivionUiActions.ToggleCardExpansion(PageId, cardId));
    }

    public void Collapse(string cardId)
    {
        Dispatch(OblivionUiActions.CollapseCard(PageId, cardId));
    }

    public void Select(string cardId)
    {
        Dispatch(OblivionUiActions.SelectCard(PageId, cardId));
    }

    public void SetPageScrollOffset(double offset)
    {
        SetSessionState(Session.WithMainScrollOffset(PageId, Math.Max(0, offset)));
    }

    public void SetLayout(OblivionViewportLayoutMode mode)
    {
        OblivionCommandId command = mode switch
        {
            OblivionViewportLayoutMode.Single => OblivionCommandId.LayoutSingle,
            OblivionViewportLayoutMode.VerticalSplit => OblivionCommandId.LayoutVerticalSplit,
            OblivionViewportLayoutMode.HorizontalSplit => OblivionCommandId.LayoutHorizontalSplit,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        RunCommand(command);
    }

    public void FocusNextSlot()
    {
        RunCommand(OblivionCommandId.LayoutFocusNext);
    }

    public void FitDiagram()
    {
        RunCommand(OblivionCommandId.DiagramFit);
    }

    public void ZoomDiagram(double factor)
    {
        string? cardId = FocusedCardId();
        if (cardId is null)
        {
            return;
        }

        OblivionDiagramViewportState state = Session.GetDiagramViewportState(cardId).ZoomBy(factor);
        SetSessionState(Session.WithDiagramViewportState(cardId, state));
    }

    public void PanDiagram(double deltaX, double deltaY)
    {
        string? cardId = FocusedCardId();
        if (cardId is null)
        {
            return;
        }

        OblivionDiagramViewportState state = Session.GetDiagramViewportState(cardId).PanBy(deltaX, deltaY);
        SetSessionState(Session.WithDiagramViewportState(cardId, state));
    }

    public void FocusSlot(OblivionViewportSlotId slotId)
    {
        OblivionViewportState viewport = Viewport.LayoutMode == OblivionViewportLayoutMode.Single
            ? Viewport with { FocusedSlot = OblivionViewportSlotId.A }
            : Viewport with { FocusedSlot = slotId };
        SetSessionState(Session.WithViewportState(PageId, viewport));
    }

    public void SetDiagramViewportState(
        string cardId,
        OblivionDiagramViewportState state)
    {
        if (!Cards.Any(card =>
                card.Kind == OblivionCardKind.Diagram &&
                string.Equals(card.Id.Value, cardId, StringComparison.Ordinal)))
        {
            return;
        }

        SetSessionState(Session.WithDiagramViewportState(cardId, state));
    }

    public void Dispatch(UiActionId actionId)
    {
        if (!OblivionUiActions.TryDecode(actionId, out OblivionInteraction? interaction) || interaction is null)
        {
            throw new InvalidOperationException($"Action '{actionId.Value}' is not an Oblivion interaction.");
        }

        OblivionSessionState state = interaction switch
        {
            OblivionInteraction.SelectCard select when IsThisCard(select.PageId, select.CardId) =>
                Session.WithSelectedCard(select.PageId, select.CardId),
            OblivionInteraction.ToggleCardExpansion toggle when IsThisCard(toggle.PageId, toggle.CardId) =>
                Session.ToggleCardExpansion(toggle.PageId, toggle.CardId),
            OblivionInteraction.CollapseCard collapse when IsThisCard(collapse.PageId, collapse.CardId) =>
                Session.CollapseCard(collapse.PageId, collapse.CardId),
            _ => throw new InvalidOperationException(
                $"Interaction '{interaction.GetType().Name}' is outside the standalone Page stack."),
        };
        SetSessionState(state);
    }

    private void RunCommand(OblivionCommandId commandId)
    {
        OblivionCommandExecutionResult result = _commands.Run(
            _application,
            _workspaceSession,
            commandId);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        }

        _workspaceSession = result.Session;
    }

    private string? FocusedCardId()
    {
        string? cardId = OblivionViewportAssignments.ResolveFocusedCardId(
            Viewport,
            Cards,
            SelectedCardId);
        return Cards.Any(card =>
            card.Kind == OblivionCardKind.Diagram &&
            string.Equals(card.Id.Value, cardId, StringComparison.Ordinal))
                ? cardId
                : null;
    }

    private void SetSessionState(OblivionSessionState state)
    {
        _workspaceSession = _workspaceSession with { State = state };
    }

    private bool IsThisCard(string pageId, string cardId)
    {
        return string.Equals(pageId, PageId, StringComparison.Ordinal) &&
            Cards.Any(card => string.Equals(card.Id.Value, cardId, StringComparison.Ordinal));
    }

    private OblivionDiagramPresentationSource? CreateDiagramPresentationSource(OblivionCard card)
    {
        if (card.Kind != OblivionCardKind.Diagram)
        {
            return null;
        }

        OblivionDiagramProjectionResult projection = _diagramRealizer.Project(card, _workspaceRoot);
        return projection.MermaidSource is null
            ? null
            : new OblivionDiagramPresentationSource(
                projection.MermaidSource,
                projection.Source.Reference,
                projection.Source.Projection.ToString(),
                Diagnostics: projection.Diagnostics);
    }

    private OblivionTablePresentationSource? CreateTablePresentationSource(OblivionCard card)
    {
        if (card.Kind != OblivionCardKind.Table)
        {
            return null;
        }

        OblivionTableCardRealization realization = new OblivionTableCardRealizer().Realize(
            card,
            _workspaceRoot);
        if (!realization.Succeeded ||
            realization.Table is null ||
            realization.Profile is null ||
            realization.SourceHash is null)
        {
            return null;
        }

        return new OblivionTablePresentationSource(
            realization.Table,
            realization.Source.Reference,
            realization.Profile,
            realization.SourceHash,
            realization.LoadMilliseconds,
            realization.Diagnostics);
    }

    private static string CardTypeLabel(OblivionCard card)
    {
        return card.Kind switch
        {
            OblivionCardKind.Diagram => "Diagram · State",
            OblivionCardKind.Table => "Table · TSON",
            _ => "Markdown",
        };
    }

    private static IReadOnlyList<OblivionCard> ValidateCards(OblivionWorkspacePage page)
    {
        if (page.Cards.Any(card =>
                card.Kind != OblivionCardKind.Diagram &&
                card.Kind != OblivionCardKind.Table &&
                card.Body.Format != OblivionCardBodyFormat.CopelandMarkdown))
        {
            throw new InvalidOperationException("The standalone Page stack must contain only Markdown, Diagram, or Table Cards.");
        }

        return page.Cards;
    }
}

public sealed record OblivionStandaloneCardPresentation(
    OblivionCard Card,
    OblivionCompactCardView CardView,
    OblivionContentPresentationPlan ContentPlan,
    bool IsSelected,
    OblivionViewportSlotId SlotId,
    OblivionDiagramViewportState DiagramViewportState);

public sealed record OblivionStandaloneCardSnapshot(
    OblivionCard Card,
    OblivionCompactCardView CardView,
    OblivionContentPresentationPlan ContentPlan,
    bool IsSelected,
    OblivionViewportSlotId SlotId,
    OblivionDiagramViewportState DiagramViewportState,
    Rect SlotBounds,
    Rect CardBounds,
    Rect ExpansionAffordanceBounds,
    Rect? MatureContentBounds)
{
    public bool MatureContentMounted => MatureContentBounds is not null;
}

public sealed record OblivionStandaloneSurfaceSnapshot(
    int Width,
    int ViewportHeight,
    int PageContentHeight,
    OblivionViewportState Viewport,
    IReadOnlyList<OblivionStandaloneSlotSnapshot> Slots,
    Aurelian.Rendering.Raster.RasterFrame ShellFrame,
    IReadOnlyList<OblivionStandaloneCardSnapshot> Cards);

public sealed record OblivionStandaloneSlotSnapshot(
    OblivionViewportSlotId SlotId,
    Rect Bounds,
    string? CardId,
    bool IsFocused);
