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

    public static UiNode Label(
        string text,
        NodeId? id = null,
        StandardTheme? theme = null)
    {
        return Components.Label.Create(
            text,
            id,
            theme);
    }

    public static UiNode Field(
        UiNode control,
        NodeId? id = null,
        string? label = null,
        string? description = null,
        string? error = null,
        StandardTheme? theme = null)
    {
        return Components.Field.Create(
            control,
            id,
            label,
            description,
            error,
            theme);
    }

    public static UiNode Input(
        NodeId? id = null,
        string? value = null,
        string? placeholder = null,
        bool disabled = false,
        UiAction? changed = null,
        StandardTheme? theme = null)
    {
        return Components.Input.Create(
            id,
            value,
            placeholder,
            disabled,
            changed,
            theme);
    }

    public static UiNode Checkbox(
        NodeId? id = null,
        string? label = null,
        bool isChecked = false,
        bool disabled = false,
        UiAction? changed = null,
        StandardTheme? theme = null)
    {
        return Components.Checkbox.Create(
            id,
            label,
            isChecked,
            disabled,
            changed,
            theme);
    }

    public static UiNode Switch(
        NodeId? id = null,
        string? label = null,
        bool isOn = false,
        bool disabled = false,
        UiAction? changed = null,
        StandardTheme? theme = null)
    {
        return Components.Switch.Create(
            id,
            label,
            isOn,
            disabled,
            changed,
            theme);
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
