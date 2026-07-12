using Machina.Core.Actions;

namespace Machina.Presenter.Sample;

public static class DemoStateDispatch
{
    public static DemoState Dispatch(DemoState state, UiActionId action)
    {
        if (action == SettingsActions.Increment)
        {
            return state with { Count = state.Count + 1 };
        }

        if (action == SettingsActions.ToggleEmailUpdates)
        {
            return state with { EmailUpdates = !state.EmailUpdates };
        }

        if (action == SettingsActions.ToggleNotifications)
        {
            return state with { Notifications = !state.Notifications };
        }

        return state;
    }
}
