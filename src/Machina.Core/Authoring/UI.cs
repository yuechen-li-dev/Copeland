using Machina.Core.Actions;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Frames;

namespace Machina.Core.Authoring;

public static class UI
{
    public static UiNode Text(
        string text,
        ColorToken? color = null,
        TextSize size = TextSize.Md,
        TextStyle? style = null)
    {
        var effectiveStyle = style ?? new TextStyle(color, size);
        return new TextNode(text, effectiveStyle);
    }

    public static UiNode Rect(
        UiNode? child = null,
        double? width = null,
        double? height = null,
        ColorToken? color = null,
        double padding = 0,
        UiStyle? style = null)
    {
        return new RectNode(child, width, height, color, padding, style);
    }

    public static UiNode Row(
        IReadOnlyList<UiNode> children,
        double gap = 0,
        double padding = 0)
    {
        return new StackNode(StackAxis.Horizontal, children, gap, padding);
    }

    public static UiNode Column(
        IReadOnlyList<UiNode> children,
        double gap = 0,
        double padding = 0)
    {
        return new StackNode(StackAxis.Vertical, children, gap, padding);
    }

    public static UiNode Container(
        UiNode child,
        Align alignX = Align.Start,
        Align alignY = Align.Start)
    {
        return new ContainerNode(child, alignX, alignY);
    }

    public static UiNode Button(
        string text,
        UiAction? action = null,
        bool disabled = false,
        ColorToken? color = null,
        UiStyle? style = null)
    {
        var effectiveStyle = style;
        if (color is not null)
        {
            effectiveStyle = (effectiveStyle ?? new UiStyle()) with
            {
                Foreground = color,
            };
        }

        return new ButtonNode(text, action, disabled, effectiveStyle);
    }

    public static UiNode HSpace(double width)
    {
        return new SpacerNode(StackAxis.Horizontal, width);
    }

    public static UiNode VSpace(double height)
    {
        return new SpacerNode(StackAxis.Vertical, height);
    }
}
