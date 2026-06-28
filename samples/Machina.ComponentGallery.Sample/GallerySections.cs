using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Authoring;
using Machina.Standard.Components;
using Machina.Standard.Text;
using Machina.Standard.Theme;
using StandardText = Machina.Standard.Text.Text;

namespace Machina.ComponentGallery.Sample;

public static class GallerySections
{
    public static UiNode Header(GalleryState state, StandardTheme theme)
    {
        return UI.Column(
            id: "header-stack",
            gap: 4,
            children:
            [
                UI.Text(
                    "Machina Component Gallery",
                    id: "gallery-title",
                    size: TextSize.H1,
                    color: theme.Colors.Foreground),

                UI.Text(
                    "StandardUI widget wall",
                    id: "gallery-subtitle",
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground),

                UI.Text(
                    $"Primary clicks: {state.PrimaryClicks} | Secondary clicks: {state.SecondaryClicks}",
                    id: "gallery-count",
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground),
            ]);
    }

    public static UiNode TextSection(StandardTheme theme)
    {
        return StandardUI.Card(
            id: "text-card",
            theme: theme,
            gap: 10,
            children:
            [
                UI.Text("Typography / Text", id: "text-title", color: theme.Colors.Foreground),

                UI.Text(
                    "Primitive UI.Text still owns titles and counts.",
                    id: "text-primitive-label",
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground),

                StandardUI.TextBlock(
                    id: "text-plain",
                    text: StandardText.Plain(
                        "Standard.Text renders wrapped copy through the layout bridge.",
                        variant: MachinaTextVariant.Body),
                    theme: theme),

                StandardUI.TextBlock(
                    id: "text-markup",
                    text: StandardText.Markup(
                        "This **markup** paragraph keeps `code` local to the assigned box.",
                        variant: MachinaTextVariant.Body),
                    theme: theme,
                    foreground: theme.Colors.Foreground),

                StandardUI.TextBlock(
                    id: "text-bullets",
                    text: StandardText.Markup(
                        """
                        - paragraphs
                        - bullets
                        - deterministic geometry
                        """,
                        variant: MachinaTextVariant.Caption),
                    theme: theme,
                    foreground: theme.Colors.MutedForeground),
            ]);
    }

    public static UiNode ButtonsSection(GalleryState state, StandardTheme theme)
    {
        return StandardUI.Card(
            id: "buttons-card",
            theme: theme,
            gap: 10,
            children:
            [
                UI.Text("Buttons", id: "buttons-title", color: theme.Colors.Foreground),

                UI.Text(
                    $"Button clicks are dispatched in plain C#: {state.PrimaryClicks + state.SecondaryClicks}",
                    id: "buttons-summary",
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground),

                StandardUI.Button(
                    "Primary",
                    id: "button-primary",
                    action: GalleryActions.ClickPrimaryButton.ToAction(),
                    theme: theme),

                StandardUI.Button(
                    "Outline",
                    id: "button-outline",
                    action: GalleryActions.ClickSecondaryButton.ToAction(),
                    variant: ButtonVariant.Outline,
                    theme: theme),
            ]);
    }

    public static UiNode SelectionSection(GalleryState state, StandardTheme theme)
    {
        return StandardUI.Card(
            id: "selection-card",
            theme: theme,
            gap: 8,
            children:
            [
                UI.Text("Checkbox / Switch", id: "selection-title", color: theme.Colors.Foreground),

                StandardUI.Checkbox(
                    id: "checkbox-unchecked",
                    label: "Unchecked",
                    isChecked: false,
                    theme: theme),

                StandardUI.Checkbox(
                    id: "checkbox-checked",
                    label: "Checked",
                    isChecked: true,
                    theme: theme),

                StandardUI.Switch(
                    id: "switch-off",
                    label: "Off",
                    isOn: false,
                    theme: theme),

                StandardUI.Switch(
                    id: "switch-on",
                    label: "On",
                    isOn: true,
                    theme: theme),
            ]);
    }

    public static UiNode ActionsSection(GalleryState state, StandardTheme theme)
    {
        return StandardUI.Card(
            id: "actions-card",
            theme: theme,
            gap: 8,
            children:
            [
                UI.Text("Interactive Probes", id: "actions-title", color: theme.Colors.Foreground),

                StandardUI.Checkbox(
                    id: "live-checkbox",
                    label: $"Live checkbox {(state.LiveCheckboxChecked ? "on" : "off")}",
                    isChecked: state.LiveCheckboxChecked,
                    changed: GalleryActions.ToggleCheckbox.ToAction(),
                    theme: theme),

                StandardUI.Switch(
                    id: "live-switch",
                    label: $"Live switch {(state.LiveSwitchOn ? "on" : "off")}",
                    isOn: state.LiveSwitchOn,
                    changed: GalleryActions.ToggleSwitch.ToAction(),
                    theme: theme),
            ]);
    }

    public static UiNode InputSection(GalleryState state, StandardTheme theme)
    {
        var inputStyle = theme.Input.Default with
        {
            Width = 236,
        };

        return StandardUI.Card(
            id: "input-card",
            theme: theme,
            gap: 10,
            children:
            [
                UI.Text("Input", id: "input-title", color: theme.Colors.Foreground),

                StandardUI.Input(
                    id: "input-empty",
                    placeholder: "Placeholder / empty",
                    theme: theme,
                    style: inputStyle),

                StandardUI.Input(
                    id: "input-value",
                    value: state.InputValue,
                    theme: theme,
                    style: inputStyle),
            ]);
    }

    public static UiNode BadgesSection(StandardTheme theme)
    {
        return StandardUI.Card(
            id: "badges-card",
            theme: theme,
            gap: 10,
            children:
            [
                UI.Text("Badges / Separator", id: "badges-title", color: theme.Colors.Foreground),

                UI.Row(
                    id: "badges-row",
                    gap: 8,
                    children:
                    [
                        StandardUI.Badge("Stable", id: "badge-stable", theme: theme, variant: BadgeVariant.Secondary),
                        StandardUI.Badge("Alert", id: "badge-alert", theme: theme, variant: BadgeVariant.Destructive),
                    ]),

                StandardUI.Separator(id: "separator-horizontal", theme: theme),

                UI.Text(
                    "Separators remain deterministic fixed geometry.",
                    id: "separator-caption",
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground),
            ]);
    }

    public static UiNode CardsSection(StandardTheme theme)
    {
        return StandardUI.Card(
            id: "cards-shell",
            theme: theme,
            gap: 12,
            children:
            [
                UI.Text("Cards", id: "cards-title", color: theme.Colors.Foreground),

                StandardUI.Card(
                    id: "simple-card",
                    theme: theme,
                    gap: 8,
                    children:
                    [
                        UI.Text("Simple Card", id: "simple-card-title", color: theme.Colors.Foreground),
                        UI.Text(
                            "Local text plus action.",
                            id: "simple-card-copy",
                            size: TextSize.Sm,
                            color: theme.Colors.MutedForeground),
                        StandardUI.Button(
                            "Primary",
                            id: "simple-card-button",
                            action: GalleryActions.ClickPrimaryButton.ToAction(),
                            theme: theme),
                    ]),

                StandardUI.Card(
                    id: "rich-card",
                    theme: theme,
                    gap: 8,
                    children:
                    [
                        UI.Text("Card with TextBlock", id: "rich-card-title", color: theme.Colors.Foreground),
                        StandardUI.TextBlock(
                            id: "rich-card-textblock",
                            text: StandardText.Markup(
                                """
                                StandardUI cards can host **TextBlock** without changing screen layout rules.

                                - local composition
                                - explicit theme handoff
                                """,
                                variant: MachinaTextVariant.Caption),
                            theme: theme,
                            foreground: theme.Colors.MutedForeground),
                    ]),
            ]);
    }

    public static UiNode ThemeSection(StandardTheme theme)
    {
        var probeTheme = GalleryTheme.CreateProbeTheme(theme);

        return StandardUI.Card(
            id: "theme-shell",
            theme: theme,
            gap: 12,
            children:
            [
                UI.Text("Theme Probe", id: "theme-title", color: theme.Colors.Foreground),

                UI.Text(
                    "Custom theme only. No hidden cascade.",
                    id: "theme-caption",
                    size: TextSize.Sm,
                    color: theme.Colors.MutedForeground),

                StandardUI.Card(
                    id: "theme-card",
                    theme: probeTheme,
                    gap: 8,
                    children:
                    [
                        UI.Text("Custom theme", id: "theme-card-title", color: probeTheme.Colors.Foreground),
                        StandardUI.Button(
                            "Accent",
                            id: "theme-button",
                            action: GalleryActions.ClickSecondaryButton.ToAction(),
                            theme: probeTheme),
                        StandardUI.Checkbox(
                            id: "theme-checkbox",
                            label: "Probe mark color",
                            isChecked: true,
                            theme: probeTheme),
                        StandardUI.TextBlock(
                            id: "theme-textblock",
                            text: StandardText.Plain(
                                "Theme color flows through card, button, checkbox, and text.",
                                variant: MachinaTextVariant.Caption),
                            theme: probeTheme,
                            foreground: probeTheme.Colors.Foreground),
                    ]),
            ]);
    }
}
