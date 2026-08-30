using System.Collections.Immutable;
using Machina.Presentation.Input;
using Machina.Runtime.Input;

namespace Machina.Presenter.Sample;

/// <summary>
/// Presenter-specific action resolution over Machina's canonical input batch.
/// The host owns batch construction; this router has no platform or backend
/// dependency and processes the supplied event order without reordering.
/// </summary>
public static class PresenterUiInputRouter
{
    public static PresenterUiInputRoutingResult Route(
        PresenterNavigationShellRenderResult render,
        UiInputBatch inputBatch,
        ScrollbarInteractionState? interactionState)
    {
        return Route(render, inputBatch, interactionState, recompose: null);
    }

    /// <summary>
    /// Routes a single published batch in callback order. When a resize is
    /// encountered the host recomposes immediately, before any later
    /// coordinate-dependent event in that same batch is resolved.
    /// </summary>
    public static PresenterUiInputRoutingResult Route(
        PresenterNavigationShellRenderResult render,
        UiInputBatch inputBatch,
        ScrollbarInteractionState? interactionState,
        Func<UiSurfaceSize, PresenterNavigationShellRenderResult>? recompose)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(inputBatch);

        PresenterNavigationShellRenderResult currentRender = render;
        ScrollbarInteractionState currentState = interactionState
            ?? ScrollbarInteractionState.Default;
        ImmutableArray<PresenterNavigationInputRoutingResult>.Builder routedEvents =
            ImmutableArray.CreateBuilder<PresenterNavigationInputRoutingResult>();
        MachinaFrontendInputRoutingResult frontendRouting = MachinaFrontendInputRouter.Route(inputBatch);
        int recompositionCount = 0;

        foreach (UiInputEvent inputEvent in inputBatch.Events)
        {
            switch (inputEvent)
            {
                case UiSurfaceResized resized:
                    if (recompose is not null)
                    {
                        currentRender = recompose(resized.Size);
                        recompositionCount++;
                    }

                    continue;
                case UiCloseRequested:
                    continue;
            }

            PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
                currentRender,
                inputEvent,
                currentState);
            currentState = routed.InteractionState;
            routedEvents.Add(routed);
        }

        return new PresenterUiInputRoutingResult(
            inputBatch.BatchId,
            routedEvents.ToImmutable(),
            currentState,
            frontendRouting.FrontendMessages,
            frontendRouting.RequiresRecomposition,
            frontendRouting.FrontendMessages.OfType<MachinaFrontendCloseRequested>().Any(),
            recompositionCount);
    }
}

/// <summary>
/// Typed outputs produced by canonical input routing. Backend translation and
/// raster/backend selection remain integration-host work.
/// </summary>
public sealed record PresenterUiInputRoutingResult(
    ulong BatchId,
    ImmutableArray<PresenterNavigationInputRoutingResult> RoutedEvents,
    ScrollbarInteractionState InteractionState,
    ImmutableArray<MachinaFrontendMessage> FrontendMessages,
    bool RequiresRecomposition,
    bool CloseRequested,
    int RecompositionCount);
