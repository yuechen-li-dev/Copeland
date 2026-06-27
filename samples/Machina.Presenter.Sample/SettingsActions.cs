using Machina.Core.Actions;

namespace Machina.Presenter.Sample;

public static class SettingsActions
{
    public static readonly UiActionId Increment = new("counter.increment");
    public static readonly UiActionId ToggleEmailUpdates = new("settings.emailUpdates.toggle");
    public static readonly UiActionId ToggleNotifications = new("settings.notifications.toggle");
}
