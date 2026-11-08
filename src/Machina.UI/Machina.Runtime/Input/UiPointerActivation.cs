using Machina.Core.Actions;
using Machina.Layout.Rows;

namespace Machina.Runtime.Input;

/// <summary>One pointer press, owned by the containing screen's state scope.</summary>
public sealed class UiPointerActivation
{
    private NodeId? pressedNode;
    private UiAction? pressedAction;

    public void Press(UiHitTestResult? hit)
    {
        Reset();
        if (hit is not null && hit.Semantics?.Disabled != true)
        {
            pressedNode = hit.NodeId;
            pressedAction = hit.Action;
        }
    }

    public UiAction? Release(UiHitTestResult? hit)
    {
        UiAction? action = null;
        if (hit is not null && hit.Semantics?.Disabled != true
            && pressedNode == hit.NodeId && pressedAction == hit.Action)
        {
            action = pressedAction;
        }
        Reset();
        return action;
    }

    public void Reset()
    {
        pressedNode = null;
        pressedAction = null;
    }
}
