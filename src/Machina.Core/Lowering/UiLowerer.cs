using Machina.Core.Measurement;
using Machina.Core.Nodes;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Core.Lowering;

public static class UiLowerer
{
    public static UiLoweringResult Lower(
        UiNode root,
        UiLoweringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var effectiveOptions = options ?? new UiLoweringOptions();
        var context = new UiLoweringContext(effectiveOptions.EffectiveTextMeasurer);
        LowerNode(root, context, parent: null, order: 0, isRoot: true, parentIsStack: false);

        return new UiLoweringResult(
            context.Rows,
            context.Styles,
            context.TextStyles,
            context.Semantics,
            context.Actions);
    }

    private static NodeId LowerNode(
        UiNode node,
        UiLoweringContext context,
        NodeId? parent,
        int order,
        bool isRoot,
        bool parentIsStack)
    {
        var id = context.AllocateId(node.Id);
        var frame = CreateFrame(node, context, isRoot, parentIsStack);
        var arrange = CreateArrange(node);
        var debugLabel = CreateDebugLabel(node);

        context.Rows.Add(new LayoutRow(
            id,
            frame,
            parent,
            order,
            Z: 0,
            View: null,
            Slot: null,
            DebugLabel: debugLabel,
            Layer: null,
            Arrange: arrange));

        AddMetadata(node, id, context);
        AddDeclaredMetadata(node, id, context);
        LowerChildren(node, context, id);

        return id;
    }

    private static FrameSpec CreateFrame(
        UiNode node,
        UiLoweringContext context,
        bool isRoot,
        bool parentIsStack)
    {
        if (isRoot)
        {
            return new RootFrame();
        }

        if (parentIsStack)
        {
            return CreateStackChildFrame(node, context);
        }

        return CreateDirectChildFrame(node, context);
    }

    private static FrameSpec CreateStackChildFrame(UiNode node, UiLoweringContext context)
    {
        return node switch
        {
            TextNode text => CreateTextFrame(text, context),
            ButtonNode button => CreateButtonFrame(button, context),
            SpacerNode spacer => CreateSpacerFrame(spacer),
            RectNode rect => CreateRectFrame(rect),
            StackNode stack => CreateStackPlaceholderFrame(stack),
            ContainerNode => new FillFrame(),
            PlacementNode placement => placement.Frame,
            LayerNode layer => layer.Frame ?? new FillFrame(),
            _ => throw Unsupported(node),
        };
    }

    private static FrameSpec CreateDirectChildFrame(UiNode node, UiLoweringContext context)
    {
        return node switch
        {
            TextNode text => CreateDirectTextFrame(text, context),
            ButtonNode button => CreateDirectButtonFrame(button, context),
            SpacerNode spacer => new AnchorFrame(Left: 0, Width: SpacerWidth(spacer), Top: 0, Height: SpacerHeight(spacer)),
            RectNode rect => CreateDirectRectFrame(rect),
            StackNode => new AnchorFrame(Left: 0, Right: 0, Top: 0, Bottom: 0),
            ContainerNode => new AnchorFrame(Left: 0, Right: 0, Top: 0, Bottom: 0),
            PlacementNode placement => placement.Frame,
            LayerNode layer => layer.Frame ?? new AnchorFrame(Left: 0, Right: 0, Top: 0, Bottom: 0),
            _ => throw Unsupported(node),
        };
    }

    private static FrameSpec CreateRectFrame(RectNode rect)
    {
        if (rect.Width is { } width && rect.Height is { } height)
        {
            return new FixedFrame(width, height);
        }

        if (rect.Height is { } explicitHeight)
        {
            return new FixedFrame(EstimateFallbackWidth(rect), explicitHeight);
        }

        if (rect.Width is { } explicitWidth)
        {
            return new FixedFrame(explicitWidth, EstimateFallbackHeight(rect));
        }

        return new FillFrame();
    }

    private static FrameSpec CreateDirectRectFrame(RectNode rect)
    {
        if (rect.Width is { } width && rect.Height is { } height)
        {
            return new AnchorFrame(Left: 0, Width: width, Top: 0, Height: height);
        }

        if (rect.Height is { } explicitHeight)
        {
            return new AnchorFrame(Left: 0, Right: 0, Top: 0, Height: explicitHeight);
        }

        if (rect.Width is { } explicitWidth)
        {
            return new AnchorFrame(Left: 0, Width: explicitWidth, Top: 0, Bottom: 0);
        }

        return new AnchorFrame(Left: 0, Right: 0, Top: 0, Bottom: 0);
    }

    private static FrameSpec CreateStackPlaceholderFrame(StackNode stack)
    {
        var width = stack.Axis == StackAxis.Horizontal ? 240 : 160;
        var height = stack.Axis == StackAxis.Horizontal ? 48 : 160;
        return new FixedFrame(width, height);
    }

    private static FixedFrame CreateTextFrame(TextNode text, UiLoweringContext context)
    {
        var size = MeasureText(text, context);
        return new FixedFrame(size.Width, size.Height);
    }

    private static FixedFrame CreateButtonFrame(ButtonNode button, UiLoweringContext context)
    {
        var size = MeasureButton(button, context);
        return new FixedFrame(size.Width, size.Height);
    }

    private static AnchorFrame CreateDirectTextFrame(TextNode text, UiLoweringContext context)
    {
        var size = MeasureText(text, context);
        return new AnchorFrame(Left: 0, Width: size.Width, Top: 0, Height: size.Height);
    }

    private static AnchorFrame CreateDirectButtonFrame(ButtonNode button, UiLoweringContext context)
    {
        var size = MeasureButton(button, context);
        return new AnchorFrame(Left: 0, Width: size.Width, Top: 0, Height: size.Height);
    }

    private static FixedFrame CreateSpacerFrame(SpacerNode spacer)
    {
        return new FixedFrame(SpacerWidth(spacer), SpacerHeight(spacer));
    }

    private static ArrangeSpec? CreateArrange(UiNode node)
    {
        if (node is StackNode stack)
        {
            return new StackArrange(
                stack.Axis,
                stack.Gap,
                Padding: EdgeInsets.All(stack.Padding));
        }

        return null;
    }

    private static void LowerChildren(UiNode node, UiLoweringContext context, NodeId id)
    {
        switch (node)
        {
            case RectNode { Child: { } child }:
                LowerNode(child, context, id, order: 0, isRoot: false, parentIsStack: false);
                return;

            case RectNode:
                return;

            case ContainerNode container:
                LowerNode(container.Child, context, id, order: 0, isRoot: false, parentIsStack: false);
                return;

            case StackNode stack:
                for (var index = 0; index < stack.Children.Count; index++)
                {
                    LowerNode(stack.Children[index], context, id, index, isRoot: false, parentIsStack: true);
                }

                return;

            case PlacementNode placement:
                LowerNode(placement.Child, context, id, order: 0, isRoot: false, parentIsStack: false);
                return;

            case LayerNode layer:
                for (var index = 0; index < layer.Children.Count; index++)
                {
                    LowerNode(layer.Children[index], context, id, index, isRoot: false, parentIsStack: false);
                }

                return;

            case TextNode:
            case ButtonNode:
            case SpacerNode:
                return;

            default:
                throw Unsupported(node);
        }
    }

    private static void AddMetadata(UiNode node, NodeId id, UiLoweringContext context)
    {
        switch (node)
        {
            case TextNode text:
                context.TextStyles[id] = text.Style ?? new TextStyle();
                context.Semantics[id] = new UiSemantics(UiRole.Text, text.Text);
                return;

            case ButtonNode button:
                if (button.Style is not null)
                {
                    context.Styles[id] = button.Style;
                }

                context.Semantics[id] = new UiSemantics(
                    UiRole.Button,
                    button.Text,
                    Disabled: button.Disabled,
                    Focusable: !button.Disabled);

                if (!button.Disabled && button.Action is not null)
                {
                    context.Actions[id] = button.Action;
                }

                return;

            case RectNode rect:
                var style = rect.Style ?? new UiStyle();
                style = style with
                {
                    Background = rect.Color ?? style.Background,
                    Padding = rect.Padding,
                };
                context.Styles[id] = style;
                return;

            case ContainerNode:
                context.Semantics[id] = new UiSemantics(UiRole.Container);
                return;

            case StackNode:
            case SpacerNode:
                return;

            case PlacementNode:
                return;

            case LayerNode layer:
                if (layer.Style is not null)
                {
                    context.Styles[id] = layer.Style;
                }

                context.Semantics[id] = new UiSemantics(UiRole.Container);
                return;

            default:
                throw Unsupported(node);
        }
    }

    private static void AddDeclaredMetadata(
        UiNode node,
        NodeId id,
        UiLoweringContext context)
    {
        if (node.Semantics is { } semantics)
        {
            context.Semantics[id] = semantics;
        }

        if (node.Semantics is { Disabled: true })
        {
            return;
        }

        if (node.DeclaredAction is { } action)
        {
            context.Actions[id] = action;
        }
    }

    private static string CreateDebugLabel(UiNode node)
    {
        return node switch
        {
            TextNode text => $"Text: {text.Text}",
            ButtonNode button => $"Button: {button.Text}",
            RectNode => "Rect",
            StackNode stack => stack.Axis == StackAxis.Horizontal ? "Row" : "Column",
            ContainerNode container => $"Container: {container.AlignX}/{container.AlignY}",
            SpacerNode spacer => spacer.Axis == StackAxis.Horizontal ? "HSpace" : "VSpace",
            PlacementNode => "Placement",
            LayerNode => "Layer",
            _ => throw Unsupported(node),
        };
    }

    private static IntrinsicSize MeasureText(TextNode text, UiLoweringContext context)
    {
        var style = text.Style ?? new TextStyle();
        var measured = context.TextMeasurer.MeasureText(text.Text, style);
        ValidateMeasuredSize(measured, text);
        return measured;
    }

    private static IntrinsicSize MeasureButton(ButtonNode button, UiLoweringContext context)
    {
        var measuredText = context.TextMeasurer.MeasureText(button.Text, new TextStyle(Size: TextSize.Md));
        ValidateMeasuredSize(measuredText, button);

        var width = Math.Max(80, measuredText.Width + 24);
        var height = Math.Max(32, measuredText.Height + 12);
        return new IntrinsicSize(width, height);
    }

    private static void ValidateMeasuredSize(IntrinsicSize size, UiNode node)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height))
        {
            throw new UiLoweringError(
                "InvalidMeasuredSize",
                $"Measured size for UI node type '{node.GetType().Name}' must be finite.");
        }

        if (size.Width < 0 || size.Height < 0)
        {
            throw new UiLoweringError(
                "InvalidMeasuredSize",
                $"Measured size for UI node type '{node.GetType().Name}' must be non-negative.");
        }
    }

    private static double EstimateFallbackWidth(RectNode rect)
    {
        return rect.Child is null ? 100 : 200;
    }

    private static double EstimateFallbackHeight(RectNode rect)
    {
        return rect.Child is null ? 100 : 120;
    }

    private static double SpacerWidth(SpacerNode spacer)
    {
        return spacer.Axis == StackAxis.Horizontal ? spacer.Size : 0;
    }

    private static double SpacerHeight(SpacerNode spacer)
    {
        return spacer.Axis == StackAxis.Vertical ? spacer.Size : 0;
    }

    private static UiLoweringError Unsupported(UiNode node)
    {
        return new UiLoweringError(
            "UnsupportedUiNode",
            $"Unsupported UI node type '{node.GetType().Name}'.");
    }
}
