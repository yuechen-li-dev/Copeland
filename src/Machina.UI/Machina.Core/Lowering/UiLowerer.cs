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
            context.Actions,
            context.NodePayloads);
    }

    private static NodeId LowerNode(
        UiNode node,
        UiLoweringContext context,
        NodeId? parent,
        int order,
        bool isRoot,
        bool parentIsStack)
    {
        ValidateNode(node);

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
            GridNode grid => CreateGridPlaceholderFrame(grid),
            ContainerNode => new FillFrame(),
            PlacementNode placement => placement.Frame,
            LayerNode layer => layer.Frame ?? new FillFrame(),
            RichTextNode => new FillFrame(),
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
            GridNode => new AnchorFrame(Left: 0, Right: 0, Top: 0, Bottom: 0),
            ContainerNode => new AnchorFrame(Left: 0, Right: 0, Top: 0, Bottom: 0),
            PlacementNode placement => placement.Frame,
            LayerNode layer => layer.Frame ?? new AnchorFrame(Left: 0, Right: 0, Top: 0, Bottom: 0),
            RichTextNode => new AnchorFrame(Left: 0, Right: 0, Top: 0, Bottom: 0),
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

    private static FixedFrame CreateGridPlaceholderFrame(GridNode grid)
    {
        var width = EstimateGridPlaceholderMainSize(grid.Columns, fallbackPerTrack: 120);
        var height = EstimateGridPlaceholderMainSize(grid.Rows, fallbackPerTrack: 48);
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
                Padding: stack.Padding,
                Justify: stack.Justify,
                Align: stack.Align);
        }

        if (node is GridNode grid)
        {
            return new GridArrange(
                grid.Columns,
                grid.Rows,
                grid.ColumnGap,
                grid.RowGap);
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
                for (var index = 0; index < stack.Items.Count; index++)
                {
                    LowerStackItem(stack.Axis, stack.Items[index], context, id, index);
                }

                return;

            case GridNode grid:
                for (var index = 0; index < grid.Cells.Count; index++)
                {
                    LowerGridCell(grid.Cells[index], context, id, index);
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
            case RichTextNode:
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
            case GridNode:
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

            case RichTextNode richText:
                context.Semantics[id] = new UiSemantics(UiRole.Text);
                context.NodePayloads[id] = richText.Payload;
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
            GridNode => "Grid",
            ContainerNode container => $"Container: {container.AlignX}/{container.AlignY}",
            SpacerNode spacer => spacer.Axis == StackAxis.Horizontal ? "HSpace" : "VSpace",
            PlacementNode => "Placement",
            LayerNode => "Layer",
            RichTextNode => "RichText",
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

    private static void LowerStackItem(
        StackAxis axis,
        UiStackItem item,
        UiLoweringContext context,
        NodeId parentId,
        int order)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Child);

        if (item.Kind == UiStackItemKind.Auto)
        {
            LowerNode(item.Child, context, parentId, order, isRoot: false, parentIsStack: true);
            return;
        }

        var wrapperId = context.AllocateId(new NodeId($"{parentId.Value}.item-{order}"));
        var wrapperFrame = CreateExplicitStackItemFrame(axis, item, context);

        context.Rows.Add(new LayoutRow(
            wrapperId,
            wrapperFrame,
            parentId,
            order,
            Z: 0,
            View: null,
            Slot: null,
            DebugLabel: CreateStackItemDebugLabel(item),
            Layer: null,
            Arrange: null));

        LowerNode(item.Child, context, wrapperId, order: 0, isRoot: false, parentIsStack: false);
    }

    private static void LowerGridCell(
        UiGridCell cell,
        UiLoweringContext context,
        NodeId parentId,
        int order)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(cell.Child);

        var wrapperId = context.AllocateId(new NodeId($"{parentId.Value}.cell-{cell.Row}-{cell.Column}"));
        var wrapperFrame = new CellFrame(cell.Column, cell.Row);

        context.Rows.Add(new LayoutRow(
            wrapperId,
            wrapperFrame,
            parentId,
            order,
            Z: 0,
            View: null,
            Slot: null,
            DebugLabel: $"GridCell: ({cell.Row},{cell.Column})",
            Layer: null,
            Arrange: null));

        LowerNode(cell.Child, context, wrapperId, order: 0, isRoot: false, parentIsStack: false);
    }

    private static FrameSpec CreateExplicitStackItemFrame(
        StackAxis axis,
        UiStackItem item,
        UiLoweringContext context)
    {
        return item.Kind switch
        {
            UiStackItemKind.Fixed => CreateFixedStackItemFrame(axis, item, context),
            UiStackItemKind.Fill => CreateFillStackItemFrame(item),
            _ => throw new UiLoweringError("InvalidStackItemKind", $"Unsupported stack item kind '{item.Kind}'."),
        };
    }

    private static FixedFrame CreateFixedStackItemFrame(
        StackAxis axis,
        UiStackItem item,
        UiLoweringContext context)
    {
        ValidateFiniteNonNegative(item.MainSize, "InvalidStackItemMainSize", "Stack item main size must be finite and non-negative.");

        var naturalFrame = CreateStackChildFrame(item.Child, context);
        var crossSize = ResolveFixedStackItemCrossSize(axis, naturalFrame);

        return axis == StackAxis.Horizontal
            ? new FixedFrame(item.MainSize, crossSize)
            : new FixedFrame(crossSize, item.MainSize);
    }

    private static FillFrame CreateFillStackItemFrame(UiStackItem item)
    {
        ValidateFinitePositive(item.Weight, "InvalidStackItemWeight", "Stack item fill weight must be finite and greater than zero.");
        return new FillFrame(item.Weight);
    }

    private static double ResolveFixedStackItemCrossSize(
        StackAxis axis,
        FrameSpec frame)
    {
        var fallback = DefaultFixedStackItemCrossSize(axis);

        return frame switch
        {
            FixedFrame fixedFrame => axis == StackAxis.Horizontal ? fixedFrame.Height : fixedFrame.Width,
            FillFrame fillFrame when fillFrame.Cross is { } cross => cross,
            _ => fallback,
        };
    }

    private static double DefaultFixedStackItemCrossSize(StackAxis axis)
    {
        return axis == StackAxis.Horizontal ? 48 : 160;
    }

    private static double EstimateGridPlaceholderMainSize(
        IReadOnlyList<GridTrack> tracks,
        double fallbackPerTrack)
    {
        var size = 0.0;
        var sawFill = false;

        for (var index = 0; index < tracks.Count; index++)
        {
            switch (tracks[index])
            {
                case FixedGridTrack fixedTrack:
                    size += fixedTrack.Size;
                    break;
                case FillGridTrack:
                    sawFill = true;
                    size += fallbackPerTrack;
                    break;
            }
        }

        if (tracks.Count > 1)
        {
            size += 8 * (tracks.Count - 1);
        }

        if (size <= 0)
        {
            return sawFill ? fallbackPerTrack : fallbackPerTrack * Math.Max(1, tracks.Count);
        }

        return size;
    }

    private static string CreateStackItemDebugLabel(UiStackItem item)
    {
        return item.Kind switch
        {
            UiStackItemKind.Fixed => $"StackItem: Fixed({FormatDebugNumber(item.MainSize)})",
            UiStackItemKind.Fill => $"StackItem: Fill({FormatDebugNumber(item.Weight)})",
            _ => "StackItem: Auto",
        };
    }

    private static string FormatDebugNumber(double value)
    {
        return value.ToString("0.################", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ValidateFinitePositive(double value, string code, string message)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new UiLoweringError(code, message);
        }
    }

    private static void ValidateFiniteNonNegative(double value, string code, string message)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new UiLoweringError(code, message);
        }
    }

    private static void ValidateNode(UiNode node)
    {
        if (node is not GridNode grid)
        {
            return;
        }

        ValidateGridNode(grid);
    }

    private static void ValidateGridNode(GridNode grid)
    {
        ValidateTracks(grid.Columns, "InvalidGridColumns", "Grid columns must contain at least one supported track.");
        ValidateTracks(grid.Rows, "InvalidGridRows", "Grid rows must contain at least one supported track.");
        ValidateFiniteNonNegative(grid.ColumnGap, "InvalidGridColumnGap", "Grid column gap must be finite and non-negative.");
        ValidateFiniteNonNegative(grid.RowGap, "InvalidGridRowGap", "Grid row gap must be finite and non-negative.");

        var seen = new HashSet<(int Row, int Column)>();

        for (var index = 0; index < grid.Cells.Count; index++)
        {
            var cell = grid.Cells[index];
            if (cell is null)
            {
                throw new UiLoweringError("InvalidGridCell", $"Grid cell at index {index} must not be null.");
            }

            if (cell.Child is null)
            {
                throw new UiLoweringError("InvalidGridCellChild", $"Grid cell at index {index} must not have a null child.");
            }

            if (cell.Row < 0)
            {
                throw new UiLoweringError("InvalidGridCellRow", "Grid cell row must be non-negative.");
            }

            if (cell.Column < 0)
            {
                throw new UiLoweringError("InvalidGridCellColumn", "Grid cell column must be non-negative.");
            }

            if (cell.Column >= grid.Columns.Count)
            {
                throw new UiLoweringError("GridCellColumnOutOfRange", $"Grid cell column {cell.Column} is outside the declared columns.");
            }

            if (cell.Row >= grid.Rows.Count)
            {
                throw new UiLoweringError("GridCellRowOutOfRange", $"Grid cell row {cell.Row} is outside the declared rows.");
            }

            if (!seen.Add((cell.Row, cell.Column)))
            {
                throw new UiLoweringError("DuplicateGridCell", $"Duplicate grid cell at row {cell.Row}, column {cell.Column}.");
            }
        }
    }

    private static void ValidateTracks(
        IReadOnlyList<GridTrack> tracks,
        string emptyCode,
        string emptyMessage)
    {
        if (tracks.Count == 0)
        {
            throw new UiLoweringError(emptyCode, emptyMessage);
        }

        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];

            switch (track)
            {
                case null:
                    throw new UiLoweringError("InvalidGridTrack", $"Grid track at index {index} must not be null.");
                case FixedGridTrack fixedTrack:
                    ValidateFiniteNonNegative(fixedTrack.Size, "InvalidGridTrackSize", "Fixed grid track size must be finite and non-negative.");
                    break;
                case FillGridTrack fillTrack:
                    ValidateFinitePositive(fillTrack.Weight, "InvalidGridTrackWeight", "Fill grid track weight must be finite and greater than zero.");
                    break;
                default:
                    throw new UiLoweringError("InvalidGridTrack", $"Unsupported grid track type '{track.GetType().Name}'.");
            }
        }
    }

    private static UiLoweringError Unsupported(UiNode node)
    {
        return new UiLoweringError(
            "UnsupportedUiNode",
            $"Unsupported UI node type '{node.GetType().Name}'.");
    }
}
