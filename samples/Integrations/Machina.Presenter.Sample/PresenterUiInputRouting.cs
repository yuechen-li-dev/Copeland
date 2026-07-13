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
        PresenterScrollbarInteractionState? interactionState)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(inputBatch);

        PresenterScrollbarInteractionState currentState = interactionState
            ?? PresenterScrollbarInteractionState.Default;
        ImmutableArray<PresenterNavigationInputRoutingResult>.Builder routedEvents =
            ImmutableArray.CreateBuilder<PresenterNavigationInputRoutingResult>();
        MachinaFrontendInputRoutingResult frontendRouting = MachinaFrontendInputRouter.Route(inputBatch);

        foreach (UiInputEvent inputEvent in inputBatch.Events)
        {
            switch (inputEvent)
            {
                case UiSurfaceResized or UiCloseRequested:
                    continue;
            }

            PresenterNavigationInputRoutingResult routed = PresenterNavigationInputRouter.Route(
                render,
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
            frontendRouting.FrontendMessages.OfType<MachinaFrontendCloseRequested>().Any());
    }
}

/// <summary>
/// Typed outputs produced by canonical input routing. Backend translation and
/// raster/backend selection remain integration-host work.
/// </summary>
public sealed record PresenterUiInputRoutingResult(
    ulong BatchId,
    ImmutableArray<PresenterNavigationInputRoutingResult> RoutedEvents,
    PresenterScrollbarInteractionState InteractionState,
    ImmutableArray<MachinaFrontendMessage> FrontendMessages,
    bool RequiresRecomposition,
    bool CloseRequested);

internal static class UiInputEventRoutingExtensions
{
    public static bool TryGetPointerPosition(this UiInputEvent inputEvent, out PointerPoint position)
    {
        switch (inputEvent)
        {
            case UiPointerMoved moved:
                position = moved.Position;
                return true;
            case UiPointerButtonChanged button:
                position = button.Position;
                return true;
            case UiPointerWheel wheel:
                position = wheel.Position;
                return true;
            default:
                position = default;
                return false;
        }
    }

    public static bool IsPrimaryPressed(this UiInputEvent inputEvent)
    {
        return inputEvent is UiPointerButtonChanged
        {
            Button: UiPointerButton.Primary,
            IsPressed: true,
        };
    }

    public static bool IsPointerReleased(this UiInputEvent inputEvent)
    {
        return inputEvent is UiPointerButtonChanged { IsPressed: false };
    }

    public static bool IsPointerMoved(this UiInputEvent inputEvent)
    {
        return inputEvent is UiPointerMoved;
    }

    public static bool IsWheel(this UiInputEvent inputEvent, out double deltaY)
    {
        if (inputEvent is UiPointerWheel wheel)
        {
            deltaY = wheel.DeltaY;
            return true;
        }

        deltaY = 0;
        return false;
    }

    public static UiInputEvent WithPointerPosition(this UiInputEvent inputEvent, PointerPoint position)
    {
        return inputEvent switch
        {
            UiPointerMoved moved => moved with { Position = position },
            UiPointerButtonChanged button => button with { Position = position },
            UiPointerWheel wheel => wheel with { Position = position },
            _ => inputEvent,
        };
    }
}
