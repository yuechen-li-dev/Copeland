using Aurelian.Composition;
using InputMan.Core;

namespace InputMan.Aurelian;

/// <summary>Translates application focus/capture policy into InputMan map activation.</summary>
public sealed class InputContextPolicy(
    AurelianInputAdapter adapter,
    ActionMapId gameplay,
    ActionMapId ui,
    ActionMapId rebind)
{
    public void Apply(bool uiCapturesInput, bool rebinding)
    {
        if (rebinding)
        {
            adapter.SetContexts(rebind);
        }
        else if (uiCapturesInput)
        {
            adapter.SetContexts(ui);
        }
        else
        {
            adapter.SetContexts(gameplay);
        }
    }

    public void Apply(
        LayerInputRoutingResult routing,
        LayerId uiLayer,
        bool uiIsOpaque,
        bool rebinding)
    {
        ArgumentNullException.ThrowIfNull(routing);
        if (rebinding)
        {
            adapter.SetContexts(rebind);
            return;
        }
        if (uiIsOpaque)
        {
            adapter.SetContexts(ui);
            return;
        }

        bool uiHasFocusOrCapture = routing.FocusOwner == uiLayer || routing.CaptureOwner == uiLayer;
        if (uiHasFocusOrCapture)
        {
            adapter.SetContexts(ui, gameplay);
            return;
        }
        adapter.SetContexts(gameplay);
    }
}
