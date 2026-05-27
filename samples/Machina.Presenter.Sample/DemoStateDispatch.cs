using Machina.Core.Actions;

namespace Machina.Presenter.Sample;

public static class DemoStateDispatch
{
    public static DemoState Dispatch(DemoState state, UiActionId action)
    {
        if (action == DemoDocumentFactory.Actions.Increment)
        {
            return state with { Count = state.Count + 1 };
        }

        if (action == DemoDocumentFactory.Actions.ToggleEmailUpdates)
        {
            return state with { EmailUpdates = !state.EmailUpdates };
        }

        if (action == DemoDocumentFactory.Actions.ToggleNotifications)
        {
            return state with { Notifications = !state.Notifications };
        }

        return state;
    }
}
