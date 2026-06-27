using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Text;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class SettingsCard
{
    public static UiNode Build(DemoState state, StandardTheme theme)
    {
        var emailStateText = OnOff(state.EmailUpdates);
        var notificationStateText = OnOff(state.Notifications);

        return StandardUI.Card(
            id: "settings-card-content",
            theme: theme,
            gap: 10,
            children:
            [
                UI.Text(
                    "Machina Presenter",
                    id: "title",
                    size: TextSize.Md,
                    color: theme.Colors.Foreground),

                UI.Text(
                    $"Count: {state.Count}",
                    id: "count",
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground),

                StandardUI.Button(
                    "Increment",
                    id: "increment",
                    action: SettingsActions.Increment.ToAction(),
                    theme: theme),

                StandardUI.Separator(
                    id: "rule",
                    theme: theme),

                StandardUI.Checkbox(
                    id: "email-updates",
                    label: $"Email updates: {emailStateText}",
                    isChecked: state.EmailUpdates,
                    changed: SettingsActions.ToggleEmailUpdates.ToAction(),
                    theme: theme),

                StandardUI.Switch(
                    id: "notifications",
                    label: $"Notifications: {notificationStateText}",
                    isOn: state.Notifications,
                    changed: SettingsActions.ToggleNotifications.ToAction(),
                    theme: theme),

                StandardUI.TextBlock(
                    id: "rich-text-probe",
                    text: Text.Markup(
                        """
                        This card now renders **Standard.Text** through the layout bridge.

                        - wrapped text
                        - bullet list
                        - deterministic geometry
                        """,
                        variant: MachinaTextVariant.Caption),
                    theme: theme,
                    foreground: theme.Colors.MutedForeground),
            ]);
    }

    private static string OnOff(bool value)
    {
        if (value)
        {
            return "on";
        }

        return "off";
    }
}
