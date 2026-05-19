using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Switch
{
    public static UiNode Create(
        NodeId? id = null,
        string? label = null,
        bool isOn = false,
        bool disabled = false,
        UiAction? changed = null,
        StandardTheme? theme = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        var switchShell = CreateSwitchShell(id, isOn, disabled, effectiveTheme);
        var root = CreateRoot(switchShell, id, label, disabled, effectiveTheme);

        return root with
        {
            Semantics = new UiSemantics(
                UiRole.Switch,
                label,
                Disabled: disabled,
                Focusable: !disabled),
            DeclaredAction = disabled ? null : changed,
        };
    }

    private static UiNode CreateSwitchShell(
        NodeId? id,
        bool isOn,
        bool disabled,
        StandardTheme theme)
    {
        var thumb = UI.Rect(
            id: CreateChildId(id, "thumb"),
            width: 18,
            height: 18,
            style: new UiStyle(
                Background: theme.Colors.Background,
                Foreground: null,
                Padding: 0));

        var spacerWidth = isOn ? 18 : 0;
        var trackChildren = new List<UiNode>();

        if (spacerWidth > 0)
        {
            trackChildren.Add(UI.HSpace(
                spacerWidth,
                id: CreateChildId(id, "thumb-offset")));
        }

        trackChildren.Add(thumb);

        var trackContent = UI.Row(
            trackChildren,
            id: CreateChildId(id, "track-content"),
            gap: 0,
            padding: 3);

        return UI.Rect(
            child: trackContent,
            id: CreateChildId(id, "track"),
            width: 42,
            height: 24,
            style: new UiStyle(
                Background: ResolveTrackBackground(isOn, disabled, theme),
                Foreground: null,
                Padding: 0));
    }

    private static UiNode CreateRoot(
        UiNode switchShell,
        NodeId? id,
        string? label,
        bool disabled,
        StandardTheme theme)
    {
        if (string.IsNullOrEmpty(label))
        {
            return UI.Rect(
                child: switchShell,
                id: id,
                width: 42,
                height: 24,
                style: new UiStyle(
                    Background: null,
                    Foreground: ResolveLabelColor(disabled, theme),
                    Padding: 0));
        }

        var labelNode = UI.Text(
            label,
            id: CreateChildId(id, "label"),
            style: new TextStyle(
                Color: ResolveLabelColor(disabled, theme),
                Size: TextSize.Md));

        return UI.Row(
            id: id,
            gap: theme.Spacing.Sm,
            children:
            [
                switchShell,
                labelNode,
            ]);
    }

    private static ColorToken ResolveTrackBackground(
        bool isOn,
        bool disabled,
        StandardTheme theme)
    {
        if (disabled)
        {
            return theme.Colors.Muted;
        }

        if (isOn)
        {
            return theme.Colors.Primary;
        }

        return theme.Colors.Muted;
    }

    private static ColorToken ResolveLabelColor(
        bool disabled,
        StandardTheme theme)
    {
        if (disabled)
        {
            return theme.Colors.MutedForeground;
        }

        return theme.Colors.Foreground;
    }

    private static NodeId? CreateChildId(
        NodeId? id,
        string suffix)
    {
        if (id is not { } value)
        {
            return null;
        }

        return new NodeId($"{value.Value}.{suffix}");
    }
}
