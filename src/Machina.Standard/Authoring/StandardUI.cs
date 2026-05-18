using Machina.Core.Actions;
using Machina.Core.Nodes;
using Machina.Layout.Frames;
using Machina.Layout.Rows;
using Machina.Standard.Components;
using Machina.Standard.Theme;

namespace Machina.Standard.Authoring;

public static class StandardUI
{
    public static UiNode Button(
        string text,
        NodeId? id = null,
        UiAction? action = null,
        ButtonVariant variant = ButtonVariant.Default,
        ButtonSize size = ButtonSize.Medium,
        bool disabled = false,
        StandardTheme? theme = null)
    {
        return StandardButton.Create(
            text,
            id,
            action,
            variant,
            size,
            disabled,
            theme);
    }

    public static UiNode Card(
        UiNode child,
        NodeId? id = null,
        StandardTheme? theme = null,
        double? width = null,
        double? height = null)
    {
        return Components.Card.Create(
            child,
            id,
            theme,
            width,
            height);
    }

    public static UiNode Card(
        IReadOnlyList<UiNode> children,
        NodeId? id = null,
        StandardTheme? theme = null,
        double? width = null,
        double? height = null,
        double? gap = null)
    {
        return Components.Card.Create(
            children,
            id,
            theme,
            width,
            height,
            gap);
    }

    public static UiNode Badge(
        string text,
        NodeId? id = null,
        StandardTheme? theme = null,
        BadgeVariant variant = BadgeVariant.Secondary)
    {
        return Components.Badge.Create(
            text,
            id,
            theme,
            variant);
    }

    public static UiNode Separator(
        NodeId? id = null,
        StackAxis axis = StackAxis.Horizontal,
        double thickness = 1,
        StandardTheme? theme = null)
    {
        return Components.Separator.Create(
            id,
            axis,
            thickness,
            theme);
    }
}
