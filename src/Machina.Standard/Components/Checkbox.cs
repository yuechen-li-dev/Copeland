using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Standard.Theme;

namespace Machina.Standard.Components;

public static class Checkbox
{
    public static UiNode Create(
        NodeId? id = null,
        string? label = null,
        bool isChecked = false,
        bool disabled = false,
        UiAction? changed = null,
        StandardTheme? theme = null)
    {
        var effectiveTheme = theme ?? StandardTheme.Default;
        var box = CreateBox(id, isChecked, disabled, effectiveTheme);
        var root = CreateRoot(box, id, label, disabled, effectiveTheme);

        return root with
        {
            Semantics = new UiSemantics(
                UiRole.Checkbox,
                label,
                Disabled: disabled,
                Focusable: !disabled),
            DeclaredAction = disabled ? null : changed,
        };
    }

    private static UiNode CreateBox(
        NodeId? id,
        bool isChecked,
        bool disabled,
        StandardTheme theme)
    {
        var background = ResolveBoxBackground(isChecked, disabled, theme);
        var foreground = isChecked
            ? theme.Colors.PrimaryForeground
            : theme.Colors.Foreground;

        var markerBackground = isChecked
            ? foreground
            : ColorToken.Hex(0x00000000);

        var marker = UI.Anchor(
            id: CreateChildId(id, "mark-slot"),
            left: 4,
            top: 4,
            width: 10,
            height: 10,
            child: UI.Rect(
                id: CreateChildId(id, "mark"),
                width: 10,
                height: 10,
                style: new UiStyle(
                    Background: markerBackground,
                    Foreground: null,
                    Padding: 0)));

        return UI.Rect(
            child: marker,
            id: CreateChildId(id, "box"),
            width: 18,
            height: 18,
            style: new UiStyle(
                Background: background,
                Foreground: foreground,
                Padding: 0,
                BorderColor: foreground,
                BorderThickness: 1));
    }

    private static UiNode CreateRoot(
        UiNode box,
        NodeId? id,
        string? label,
        bool disabled,
        StandardTheme theme)
    {
        if (string.IsNullOrEmpty(label))
        {
            return UI.Rect(
                child: box,
                id: id,
                width: 18,
                height: 18,
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
                Size: TextSize.Sm,
                AlignX: TextAlignX.Left,
                AlignY: TextAlignY.Center));

        return UI.Row(
            id: id,
            gap: theme.Spacing.Sm,
            children:
            [
                box,
                labelNode,
            ]);
    }

    private static ColorToken ResolveBoxBackground(
        bool isChecked,
        bool disabled,
        StandardTheme theme)
    {
        if (disabled)
        {
            return theme.Colors.Muted;
        }

        if (isChecked)
        {
            return theme.Colors.Primary;
        }

        return theme.Colors.Background;
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
