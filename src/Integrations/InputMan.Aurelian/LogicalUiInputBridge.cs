using Aurelian.Composition;
using InputMan.Core;

namespace InputMan.Aurelian;

/// <summary>Maps device-neutral UI actions to the existing Machina layer-input contract.</summary>
public sealed class LogicalUiInputBridge(
    ActionId confirm,
    ActionId cancel,
    ActionId? navigateUp = null,
    ActionId? navigateDown = null,
    ActionId? navigateLeft = null,
    ActionId? navigateRight = null)
{
    public IReadOnlyList<LayerInputRoutingResult> Route(
        InputFrame frame,
        Func<LayerInputEvent, LayerInputRoutingResult> routeInput)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(routeInput);
        var results = new List<LayerInputRoutingResult>();
        RoutePressed(frame, confirm, LayerKey.Enter, routeInput, results);
        RoutePressed(frame, cancel, LayerKey.Escape, routeInput, results);
        RoutePressed(frame, navigateUp, LayerKey.ArrowUp, routeInput, results);
        RoutePressed(frame, navigateDown, LayerKey.ArrowDown, routeInput, results);
        RoutePressed(frame, navigateLeft, LayerKey.ArrowLeft, routeInput, results);
        RoutePressed(frame, navigateRight, LayerKey.ArrowRight, routeInput, results);
        return results;
    }

    private static void RoutePressed(
        InputFrame frame,
        ActionId? action,
        LayerKey key,
        Func<LayerInputEvent, LayerInputRoutingResult> routeInput,
        List<LayerInputRoutingResult> results)
    {
        if (action is not ActionId id || !frame.WasPressed(id)) return;
        results.Add(routeInput(new LayerKeyChanged(key, true)));
        results.Add(routeInput(new LayerKeyChanged(key, false)));
    }
}
