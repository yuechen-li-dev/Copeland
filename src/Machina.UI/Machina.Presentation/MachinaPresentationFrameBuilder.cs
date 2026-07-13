using Machina.Core.Lowering;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Projection;
using Machina.Layout.Rows;
using Machina.Standard.Components;
using Machina.Standard.Text;

namespace Machina.Presentation;

/// <summary>
/// The single Machina traversal that lowers resolved UI artifacts into presentation intent.
/// </summary>
public static class MachinaPresentationFrameBuilder
{
    public static MachinaPresentationFrame Build(
        UiLoweringResult lowering,
        ResolvedLayoutDocument resolved,
        MachinaPresentationViewport? viewport = null)
    {
        ArgumentNullException.ThrowIfNull(lowering);
        ArgumentNullException.ThrowIfNull(resolved);

        MachinaPresentationViewport frameViewport = viewport ?? ResolveViewport(resolved);
        var operations = new List<MachinaPresentationOperation>();
        ResolvedLayoutTree tree = ResolvedLayoutTreeBuilder.ToResolvedTree(resolved);
        EmitNodeOperations(tree, lowering, resolved, operations);
        return new MachinaPresentationFrame(frameViewport, operations);
    }

    private static void EmitNodeOperations(
        ResolvedLayoutTree tree,
        UiLoweringResult lowering,
        ResolvedLayoutDocument resolved,
        ICollection<MachinaPresentationOperation> operations)
    {
        var node = new ResolvedLayoutNode(
            tree.Id,
            tree.Rect,
            tree.Frame,
            tree.Order,
            tree.Z,
            tree.View,
            tree.Slot,
            tree.DebugLabel,
            tree.Layer,
            tree.Arrange);

        EmitFillAndStrokeOperations(node, lowering.Styles, operations);

        bool pushClip = lowering.Styles.TryGetValue(node.Id, out UiStyle? style) && style.ClipToBounds;
        if (pushClip)
        {
            operations.Add(new PushRectangularClipOperation(node.Id.Value, node.Rect));
        }

        if (!EmitRichTextOperations(node, lowering.NodePayloads, operations))
        {
            EmitTextOperation(node, resolved, lowering.TextStyles, lowering.Semantics, operations);
        }

        foreach (ResolvedLayoutTree child in tree.Children)
        {
            EmitNodeOperations(child, lowering, resolved, operations);
        }

        if (pushClip)
        {
            operations.Add(new PopClipOperation());
        }
    }

    private static void EmitFillAndStrokeOperations(
        ResolvedLayoutNode node,
        IReadOnlyDictionary<NodeId, UiStyle> styles,
        ICollection<MachinaPresentationOperation> operations)
    {
        if (!styles.TryGetValue(node.Id, out UiStyle? style))
        {
            return;
        }

        if (style.Background is not null)
        {
            operations.Add(new FillRectangleOperation(node.Id.Value, node.Rect, style.Background.Value));
        }

        ValidateBorderThickness(style.BorderThickness, node.Id);
        if (style.BorderColor is not null && style.BorderThickness > 0)
        {
            operations.Add(new StrokeRectangleOperation(
                node.Id.Value,
                node.Rect,
                style.BorderColor.Value,
                style.BorderThickness));
        }
    }

    private static bool EmitRichTextOperations(
        ResolvedLayoutNode node,
        IReadOnlyDictionary<NodeId, object> nodePayloads,
        ICollection<MachinaPresentationOperation> operations)
    {
        if (!nodePayloads.TryGetValue(node.Id, out object? payload) ||
            payload is not StandardTextBlockMetadata metadata)
        {
            return false;
        }

        MachinaTextLayoutResult layout = MachinaTextLayoutEngine.Layout(
            metadata.Text,
            new MachinaTextBox(node.Rect.X, node.Rect.Y, node.Rect.Width, node.Rect.Height),
            MachinaTextMeasurers.Deterministic);

        var textStyle = new MachinaTextPresentationStyle(
            new TextStyle(
                Color: metadata.Foreground,
                Size: TextSize.Md,
                AlignX: TextAlignX.Left,
                AlignY: TextAlignY.Top),
            LinkColor: metadata.LinkForeground);

        foreach (PositionedTextOperation operation in MachinaTextPresentationBuilder.Build(node.Id.Value, layout, textStyle))
        {
            operations.Add(operation);
        }

        return true;
    }

    private static void EmitTextOperation(
        ResolvedLayoutNode node,
        ResolvedLayoutDocument resolved,
        IReadOnlyDictionary<NodeId, TextStyle> textStyles,
        IReadOnlyDictionary<NodeId, UiSemantics> semantics,
        ICollection<MachinaPresentationOperation> operations)
    {
        if (!semantics.TryGetValue(node.Id, out UiSemantics? semantic) ||
            !ShouldDrawText(semantic) ||
            string.IsNullOrWhiteSpace(semantic.Label))
        {
            return;
        }

        TextStyle style = textStyles.TryGetValue(node.Id, out TextStyle? textStyle)
            ? textStyle
            : new TextStyle();
        Rect rect = ResolveTextRect(node, resolved, style);
        operations.Add(new PositionedTextOperation(
            node.Id.Value,
            rect,
            semantic.Label,
            style,
            MachinaTextPresentationBuilder.ResolveColor(style)));
    }

    private static bool ShouldDrawText(UiSemantics semantic)
    {
        return semantic.Role == UiRole.Text || semantic.Role == UiRole.Label;
    }

    private static Rect ResolveTextRect(ResolvedLayoutNode node, ResolvedLayoutDocument resolved, TextStyle style)
    {
        if (!node.Id.Value.EndsWith(".label", StringComparison.Ordinal) ||
            (style.AlignX != TextAlignX.Center && style.AlignY != TextAlignY.Center))
        {
            return node.Rect;
        }

        var labelRegionId = new NodeId($"{node.Id.Value}-region");
        return resolved.Nodes.TryGetValue(labelRegionId, out ResolvedLayoutNode? labelRegion)
            ? labelRegion.Rect
            : node.Rect;
    }

    private static MachinaPresentationViewport ResolveViewport(ResolvedLayoutDocument resolved)
    {
        Rect rootRect = resolved.Nodes[resolved.RootId].Rect;
        return new MachinaPresentationViewport(
            (int)Math.Ceiling(rootRect.Width),
            (int)Math.Ceiling(rootRect.Height));
    }

    private static void ValidateBorderThickness(double thickness, NodeId nodeId)
    {
        if (!double.IsFinite(thickness))
        {
            throw new InvalidOperationException($"BorderThickness for node '{nodeId.Value}' must be finite.");
        }

        if (thickness < 0)
        {
            throw new InvalidOperationException($"BorderThickness for node '{nodeId.Value}' must be non-negative.");
        }
    }
}
