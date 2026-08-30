using Oblivion.Model;
using Oblivion.Product;

namespace Oblivion.App;

public sealed record OblivionInteractionDispatchResult(
    OblivionHostState State,
    bool Applied,
    string? DiagnosticCode,
    string? Message)
{
    public static OblivionInteractionDispatchResult Unchanged(
        OblivionHostState state,
        string diagnosticCode,
        string message)
    {
        return new OblivionInteractionDispatchResult(
            state,
            Applied: false,
            diagnosticCode,
            message);
    }
}

public static class OblivionInteractionDispatcher
{
    public static OblivionInteractionDispatchResult Dispatch(
        OblivionHostState state,
        OblivionInteraction interaction,
        OblivionHostOptions options,
        OblivionHostLayout layout)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(layout);

        return interaction switch
        {
            OblivionInteraction.SelectCard select => SelectCard(state, select, options),
            OblivionInteraction.ToggleCardExpansion toggle => ToggleExpansion(state, toggle, options),
            OblivionInteraction.CollapseCard collapse => CollapseCard(state, collapse, options),
            OblivionInteraction.SetScrollOffset scroll => SetScrollOffset(state, scroll, options, layout),
            OblivionInteraction.SelectCompactCard select => SelectCompactCard(state, select, options),
            OblivionInteraction.ClearCardSelection clear => Apply(
                state,
                state.Session.ClearSelectedCard(clear.PageId)),
            OblivionInteraction.SetCompactPane pane => Apply(
                state,
                state.Session with
                {
                    InspectorPaneSelected = pane.Pane == OblivionCompactPane.Inspector,
                }),
            OblivionInteraction.InvokeProductAction invoke => InvokeProductAction(state, invoke, options),
            _ => OblivionInteractionDispatchResult.Unchanged(
                state,
                "OBLIVION-INTERACTION-UNKNOWN",
                $"Unknown interaction type '{interaction.GetType().Name}'."),
        };
    }

    private static OblivionInteractionDispatchResult SelectCard(
        OblivionHostState state,
        OblivionInteraction.SelectCard interaction,
        OblivionHostOptions options)
    {
        if (!TryResolveCard(interaction.PageId, interaction.CardId, options, out _, out string cardId))
        {
            return MissingCard(state, interaction.PageId, interaction.CardId);
        }

        return Apply(state, state.Session.WithSelectedCard(interaction.PageId, cardId));
    }

    private static OblivionInteractionDispatchResult ToggleExpansion(
        OblivionHostState state,
        OblivionInteraction.ToggleCardExpansion interaction,
        OblivionHostOptions options)
    {
        if (!TryResolveCard(interaction.PageId, interaction.CardId, options, out OblivionCard? card, out string cardId))
        {
            return MissingCard(state, interaction.PageId, interaction.CardId);
        }

        OblivionSessionState selected = state.Session.WithSelectedCard(interaction.PageId, cardId);
        bool isExpanded = selected.GetCardViewState(interaction.PageId, cardId).IsExpanded;
        if (card!.Body.Format == OblivionCardBodyFormat.CopelandMarkdown && !isExpanded)
        {
            string[] markdownCardIds = OblivionWorkbench
                .GetPageCardsForSelection(interaction.PageId, options)
                .Where(candidate => candidate.Body.Format == OblivionCardBodyFormat.CopelandMarkdown)
                .Select(candidate => candidate.Id.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return Apply(
                state,
                selected.ExpandCardExclusively(interaction.PageId, cardId, markdownCardIds));
        }

        return Apply(state, selected.ToggleCardExpansion(interaction.PageId, cardId));
    }

    private static OblivionInteractionDispatchResult CollapseCard(
        OblivionHostState state,
        OblivionInteraction.CollapseCard interaction,
        OblivionHostOptions options)
    {
        if (!TryResolveCard(interaction.PageId, interaction.CardId, options, out _, out string cardId))
        {
            return MissingCard(state, interaction.PageId, interaction.CardId);
        }

        return Apply(state, state.Session.CollapseCard(interaction.PageId, cardId));
    }

    private static OblivionInteractionDispatchResult SetScrollOffset(
        OblivionHostState state,
        OblivionInteraction.SetScrollOffset interaction,
        OblivionHostOptions options,
        OblivionHostLayout layout)
    {
        OblivionScrollTarget target = interaction.Target;
        double clamped;
        OblivionSessionState session;

        switch (target.Kind)
        {
            case OblivionScrollTargetKind.MainCardStack:
                clamped = OblivionWorkbench.ClampMainCardStackScrollOffset(
                    target.PageId,
                    interaction.Offset,
                    options,
                    state,
                    layout);
                session = state.Session.WithMainScrollOffset(target.PageId, clamped);
                break;
            case OblivionScrollTargetKind.InspectorPane:
                clamped = OblivionWorkbench.ClampInspectorScrollOffset(
                    target.PageId,
                    interaction.Offset,
                    options,
                    state,
                    layout);
                session = state.Session.WithInspectorScrollOffset(target.PageId, clamped);
                break;
            case OblivionScrollTargetKind.ExpandedMarkdownBody:
                if (!TryResolveCardId(target, options, out string bodyCardId))
                {
                    return MissingCard(state, target.PageId, target.CardId);
                }

                clamped = OblivionWorkbench.ClampBodyScrollOffset(
                    target.PageId,
                    bodyCardId,
                    interaction.Offset,
                    options,
                    state,
                    layout);
                session = state.Session
                    .WithSelectedCard(target.PageId, bodyCardId)
                    .WithCardBodyScrollOffset(target.PageId, bodyCardId, clamped);
                break;
            case OblivionScrollTargetKind.InspectorRawMarkdownSource:
                if (!TryResolveCardId(target, options, out string sourceCardId))
                {
                    return MissingCard(state, target.PageId, target.CardId);
                }

                clamped = OblivionWorkbench.ClampRawMarkdownSourceScrollOffset(
                    target.PageId,
                    sourceCardId,
                    interaction.Offset,
                    options,
                    state,
                    layout);
                session = state.Session.WithRawSourceScrollOffset(sourceCardId, clamped);
                break;
            default:
                return OblivionInteractionDispatchResult.Unchanged(
                    state,
                    "OBLIVION-SCROLL-TARGET-UNKNOWN",
                    $"Unknown scroll target '{target.Kind}'.");
        }

        return Apply(state, session);
    }

    private static OblivionInteractionDispatchResult SelectCompactCard(
        OblivionHostState state,
        OblivionInteraction.SelectCompactCard interaction,
        OblivionHostOptions options)
    {
        if (!TryResolveCard(interaction.PageId, interaction.CardId, options, out _, out string cardId))
        {
            return MissingCard(state, interaction.PageId, interaction.CardId);
        }

        OblivionSessionState session = state.Session
            .WithSelectedCard(interaction.PageId, cardId) with
        {
            InspectorPaneSelected = true,
        };
        return Apply(state, session);
    }

    private static OblivionInteractionDispatchResult InvokeProductAction(
        OblivionHostState state,
        OblivionInteraction.InvokeProductAction interaction,
        OblivionHostOptions options)
    {
        OblivionCardActionInvocation invocation = interaction.Invocation;
        if (!TryResolveCard(invocation.PageId, invocation.CardId.Value, options, out OblivionCard? card, out string cardId))
        {
            return MissingCard(state, invocation.PageId, invocation.CardId.Value);
        }

        OblivionActionOutcome? outcome = new OblivionApplication().Invoke(
            card!,
            invocation.PageId,
            invocation.ActionId,
            state.Application);
        if (outcome is null)
        {
            return OblivionInteractionDispatchResult.Unchanged(
                state,
                "OBLIVION-ACTION-UNKNOWN",
                $"Card '{cardId}' does not declare enabled effect action '{invocation.ActionId.Value}'.");
        }

        return new OblivionInteractionDispatchResult(
            new OblivionHostState(
                state.Session.WithSelectedCard(invocation.PageId, cardId),
                outcome.State),
            Applied: true,
            DiagnosticCode: null,
            Message: null);
    }

    private static bool TryResolveCardId(
        OblivionScrollTarget target,
        OblivionHostOptions options,
        out string cardId)
    {
        if (string.IsNullOrWhiteSpace(target.CardId))
        {
            cardId = string.Empty;
            return false;
        }

        return TryResolveCard(target.PageId, target.CardId, options, out _, out cardId);
    }

    private static bool TryResolveCard(
        string pageId,
        string requestedCardId,
        OblivionHostOptions options,
        out OblivionCard? card,
        out string resolvedCardId)
    {
        IReadOnlyList<OblivionCard> cards = OblivionWorkbench.GetPageCardsForSelection(pageId, options);
        string candidateCardId = OblivionWorkbench.ResolveCardSelectionId(pageId, requestedCardId, options);
        card = cards.FirstOrDefault(candidate =>
            string.Equals(candidate.Id.Value, candidateCardId, StringComparison.Ordinal));
        resolvedCardId = candidateCardId;
        return card is not null;
    }

    private static OblivionInteractionDispatchResult MissingCard(
        OblivionHostState state,
        string pageId,
        string? cardId)
    {
        return OblivionInteractionDispatchResult.Unchanged(
            state,
            "OBLIVION-INTERACTION-CARD-MISSING",
            $"Interaction target card '{cardId ?? "<missing>"}' does not exist on page '{pageId}'.");
    }

    private static OblivionInteractionDispatchResult Apply(
        OblivionHostState state,
        OblivionSessionState session)
    {
        return new OblivionInteractionDispatchResult(
            state with { Session = session },
            Applied: true,
            DiagnosticCode: null,
            Message: null);
    }
}
