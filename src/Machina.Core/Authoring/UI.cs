using Machina.Core.Actions;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Core.Authoring;

public static class UI
{
    public static UiNode Text(
        string text,
        NodeId? id = null,
        ColorToken? color = null,
        TextSize size = TextSize.Md,
        TextAlignX alignX = TextAlignX.Left,
        TextAlignY alignY = TextAlignY.Top,
        TextStyle? style = null)
    {
        var effectiveStyle = MergeTextStyle(style, color, size, alignX, alignY);
        return new TextNode(text, effectiveStyle) with
        {
            Id = id,
        };
    }

    public static UiNode Rect(
        UiNode? child = null,
        NodeId? id = null,
        double? width = null,
        double? height = null,
        ColorToken? color = null,
        double? padding = null,
        ColorToken? borderColor = null,
        double? borderThickness = null,
        UiStyle? style = null)
    {
        var effectiveStyle = MergeBoxStyle(style, color, padding, foreground: null, borderColor, borderThickness);

        return new RectNode(
            child,
            width,
            height,
            Color: null,
            effectiveStyle.Padding,
            effectiveStyle) with
        {
            Id = id,
        };
    }

    public static UiNode Row(
        IReadOnlyList<UiNode> children,
        NodeId? id = null,
        double gap = 0,
        double padding = 0)
    {
        return new StackNode(StackAxis.Horizontal, children, gap, padding) with
        {
            Id = id,
        };
    }

    public static UiNode Column(
        IReadOnlyList<UiNode> children,
        NodeId? id = null,
        double gap = 0,
        double padding = 0)
    {
        return new StackNode(StackAxis.Vertical, children, gap, padding) with
        {
            Id = id,
        };
    }

    public static UiNode Container(
        UiNode child,
        NodeId? id = null,
        Align alignX = Align.Start,
        Align alignY = Align.Start)
    {
        return new ContainerNode(child, alignX, alignY) with
        {
            Id = id,
        };
    }

    public static UiNode Button(
        string text,
        NodeId? id = null,
        UiAction? action = null,
        bool disabled = false,
        ColorToken? color = null,
        UiStyle? style = null)
    {
        var effectiveStyle = MergeButtonStyle(style, color);

        return new ButtonNode(text, action, disabled, effectiveStyle) with
        {
            Id = id,
        };
    }

    public static UiNode HSpace(
        double width,
        NodeId? id = null)
    {
        return new SpacerNode(StackAxis.Horizontal, width) with
        {
            Id = id,
        };
    }

    public static UiNode VSpace(
        double height,
        NodeId? id = null)
    {
        return new SpacerNode(StackAxis.Vertical, height) with
        {
            Id = id,
        };
    }

    public static UiNode Surface(
        NodeId? id = null,
        double width = 0,
        double height = 0,
        ColorToken? color = null,
        UiStyle? style = null,
        IReadOnlyList<UiNode>? children = null)
    {
        var effectiveStyle = MergeBoxStyle(style, color, padding: null, foreground: null, borderColor: null, borderThickness: null);

        return new LayerNode(
            Frame: new RootFrame(),
            Style: effectiveStyle,
            Children: children ?? [],
            Width: width > 0 ? width : null,
            Height: height > 0 ? height : null) with
        {
            Id = id,
        };
    }

    public static UiNode Layer(
        NodeId? id = null,
        FrameSpec? frame = null,
        UiStyle? style = null,
        IReadOnlyList<UiNode>? children = null)
    {
        return new LayerNode(
            Frame: frame,
            Style: style,
            Children: children ?? []) with
        {
            Id = id,
        };
    }

    public static UiNode At(
        UiNode child,
        NodeId? id = null,
        double x = 0,
        double y = 0,
        double width = 0,
        double height = 0)
    {
        return new PlacementNode(
            new AbsoluteFrame(x, y, width, height),
            child) with
        {
            Id = id,
        };
    }

    public static UiNode Anchor(
        UiNode child,
        NodeId? id = null,
        UiLength? left = null,
        UiLength? right = null,
        UiLength? top = null,
        UiLength? bottom = null,
        UiLength? width = null,
        UiLength? height = null)
    {
        return new PlacementNode(
            new AnchorFrame(left, right, top, bottom, width, height),
            child) with
        {
            Id = id,
        };
    }

    private static TextStyle MergeTextStyle(
        TextStyle? style,
        ColorToken? color,
        TextSize size,
        TextAlignX alignX,
        TextAlignY alignY)
    {
        var effectiveStyle = style ?? new TextStyle();

        return effectiveStyle with
        {
            Color = color ?? effectiveStyle.Color,
            Size = size,
            AlignX = alignX,
            AlignY = alignY,
        };
    }

    private static UiStyle MergeBoxStyle(
        UiStyle? style,
        ColorToken? background,
        double? padding,
        ColorToken? foreground,
        ColorToken? borderColor,
        double? borderThickness)
    {
        var effectiveStyle = style ?? new UiStyle();

        return effectiveStyle with
        {
            Background = background ?? effectiveStyle.Background,
            Foreground = foreground ?? effectiveStyle.Foreground,
            Padding = padding ?? effectiveStyle.Padding,
            BorderColor = borderColor ?? effectiveStyle.BorderColor,
            BorderThickness = borderThickness ?? effectiveStyle.BorderThickness,
        };
    }

    private static UiStyle? MergeButtonStyle(
        UiStyle? style,
        ColorToken? color)
    {
        if (style is null && color is null)
        {
            return null;
        }

        var effectiveStyle = style ?? new UiStyle();

        return effectiveStyle with
        {
            Foreground = color ?? effectiveStyle.Foreground,
        };
    }
}
