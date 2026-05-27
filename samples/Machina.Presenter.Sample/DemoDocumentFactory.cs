using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Flat;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Theme;

namespace Machina.Presenter.Sample;

public static class DemoDocumentFactory
{
    public const int RootWidth = 640;
    public const int RootHeight = 360;

    public static class Actions
    {
        public static readonly UiActionId Increment = new("counter.increment");
        public static readonly UiActionId ToggleEmailUpdates = new("settings.emailUpdates.toggle");
        public static readonly UiActionId ToggleNotifications = new("settings.notifications.toggle");
    }

    public static UiDocument Build(DemoState state, StandardTheme? theme = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;

        return UiDocument.Create(
            rows:
            [
                Row.Root(
                    id: "root",
                    view: View.Rect(background: ColorToken.Hex(0xEDEDF0FF), foreground: ColorToken.Hex(0x09090BFF))),
                Row.Anchor(
                    id: "settings-card",
                    parent: "root",
                    left: 72,
                    top: 24,
                    width: 500,
                    height: 292,
                    component: SettingsCard(state, effectiveTheme))
            ]);
    }

    private static UiNode SettingsCard(DemoState state, StandardTheme theme)
    {
        return StandardUI.Card(
            id: "settings-card-content",
            child: UI.Column(
                id: "settings-card-column",
                gap: 10,
                children:
                [
                    UI.Text("Machina Presenter", id: "title", size: TextSize.Md),
                    UI.Text($"Count: {state.Count}", id: "count", size: TextSize.Sm),
                    StandardUI.Button("Increment", id: "increment", action: Actions.Increment.ToAction(), style: theme.Button.Default),
                    StandardUI.Separator(id: "rule"),
                    StandardUI.Checkbox(
                        id: "email-updates",
                        label: $"Email updates: {OnOff(state.EmailUpdates)}",
                        isChecked: state.EmailUpdates,
                        changed: Actions.ToggleEmailUpdates.ToAction()),
                    StandardUI.Switch(
                        id: "notifications",
                        label: $"Notifications: {OnOff(state.Notifications)}",
                        isOn: state.Notifications,
                        changed: Actions.ToggleNotifications.ToAction()),
                    UI.Text("Deterministic sample UI", id: "footnote", size: TextSize.Sm)
                ]),
            style: theme.Card.Default);
    }

    private static string OnOff(bool value)
    {
        return value ? "on" : "off";
    }
}

public sealed record DemoState(int Count, bool EmailUpdates, bool Notifications);
