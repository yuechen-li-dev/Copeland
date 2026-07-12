using Dominatus.Core.Runtime;
using Machina.Core.Lowering;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Documents;
using Machina.Layout.Projection;
using Machina.Layout.Rows;
using Machina.Standard.Components;
using Machina.Standard.Text;

namespace Machina.Dominatus.Rendering.Bridge;

public static class MachinaRenderBridge
{
    public static IReadOnlyList<IActuationCommand> BuildCommands(
        UiLoweringResult lowering,
        ResolvedLayoutDocument resolved,
        MachinaRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(lowering);
        ArgumentNullException.ThrowIfNull(resolved);

        var renderOptions = ResolveOptions(resolved, options);
        ValidateOptions(renderOptions);

        var commands = new List<IActuationCommand>
        {
            new BeginFrameCommand(renderOptions.Width, renderOptions.Height)
        };

        var tree = ResolvedLayoutTreeBuilder.ToResolvedTree(resolved);
        EmitNodeCommands(tree, lowering, resolved, commands);

        commands.Add(new EndFrameCommand());
        return commands;
    }

    private static void EmitNodeCommands(
        ResolvedLayoutTree tree,
        UiLoweringResult lowering,
        ResolvedLayoutDocument resolved,
        ICollection<IActuationCommand> commands)
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
        EmitFillAndStrokeCommands(node, lowering.Styles, commands);

        bool pushClip = lowering.Styles.TryGetValue(node.Id, out UiStyle? style) &&
            style.ClipToBounds;
        if (pushClip)
        {
            commands.Add(new PushClipCommand(node.Id.Value, node.Rect));
        }

        if (!EmitRichTextCommands(node, lowering.NodePayloads, commands))
        {
            EmitTextCommand(node, resolved, lowering.TextStyles, lowering.Semantics, commands);
        }

        foreach (ResolvedLayoutTree child in tree.Children)
        {
            EmitNodeCommands(child, lowering, resolved, commands);
        }

        if (pushClip)
        {
            commands.Add(new PopClipCommand());
        }
    }

    private static void EmitFillAndStrokeCommands(
        ResolvedLayoutNode node,
        IReadOnlyDictionary<NodeId, UiStyle> styles,
        ICollection<IActuationCommand> commands)
    {
        if (!styles.TryGetValue(node.Id, out var style))
        {
            return;
        }

        if (style.Background is not null)
        {
            commands.Add(new FillRectCommand(node.Id.Value, node.Rect, style.Background.Value));
        }

        ValidateBorderThickness(style.BorderThickness, node.Id);

        if (style.BorderColor is not null && style.BorderThickness > 0)
        {
            commands.Add(new StrokeRectCommand(node.Id.Value, node.Rect, style.BorderColor.Value, style.BorderThickness));
        }
    }

    private static void ValidateBorderThickness(double thickness, NodeId nodeId)
    {
        if (double.IsNaN(thickness) || double.IsInfinity(thickness))
        {
            throw new InvalidOperationException($"BorderThickness for node '{nodeId.Value}' must be finite.");
        }

        if (thickness < 0)
        {
            throw new InvalidOperationException($"BorderThickness for node '{nodeId.Value}' must be non-negative.");
        }
    }

    private static bool EmitRichTextCommands(
        ResolvedLayoutNode node,
        IReadOnlyDictionary<NodeId, object> nodePayloads,
        ICollection<IActuationCommand> commands)
    {
        if (!nodePayloads.TryGetValue(node.Id, out var payload))
        {
            return false;
        }

        if (payload is not StandardTextBlockMetadata metadata)
        {
            return false;
        }

        var layout = MachinaTextLayoutEngine.Layout(
            metadata.Text,
            new MachinaTextBox(node.Rect.X, node.Rect.Y, node.Rect.Width, node.Rect.Height),
            MachinaTextMeasurers.Deterministic);

        var renderStyle = new MachinaTextRenderStyle(
            BaseStyle: new TextStyle(
                Color: metadata.Foreground,
                Size: TextSize.Md,
                AlignX: TextAlignX.Left,
                AlignY: TextAlignY.Top),
            LinkColor: metadata.LinkForeground);

        foreach (var command in MachinaTextRenderBridge.ToDrawTextCommands(node.Id.Value, layout, renderStyle))
        {
            commands.Add(command);
        }

        return true;
    }

    private static void EmitTextCommand(
        ResolvedLayoutNode node,
        ResolvedLayoutDocument resolved,
        IReadOnlyDictionary<NodeId, TextStyle> textStyles,
        IReadOnlyDictionary<NodeId, UiSemantics> semantics,
        ICollection<IActuationCommand> commands)
    {
        if (!semantics.TryGetValue(node.Id, out var semantic))
        {
            return;
        }

        if (!ShouldDrawText(semantic))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(semantic.Label))
        {
            return;
        }

        var style = textStyles.TryGetValue(node.Id, out var textStyle)
            ? textStyle
            : new TextStyle();

        var rect = ResolveTextRect(node, resolved, style);
        commands.Add(new DrawTextCommand(node.Id.Value, rect, semantic.Label, style));
    }

    private static bool ShouldDrawText(UiSemantics semantic)
    {
        return semantic.Role == UiRole.Text
            || semantic.Role == UiRole.Label;
    }

    private static Machina.Layout.Geometry.Rect ResolveTextRect(
        ResolvedLayoutNode node,
        ResolvedLayoutDocument resolved,
        TextStyle style)
    {
        if (!node.Id.Value.EndsWith(".label", StringComparison.Ordinal))
        {
            return node.Rect;
        }

        if (style.AlignX != TextAlignX.Center && style.AlignY != TextAlignY.Center)
        {
            return node.Rect;
        }

        var labelRegionId = new NodeId($"{node.Id.Value}-region");
        if (resolved.Nodes.TryGetValue(labelRegionId, out var labelRegion))
        {
            return labelRegion.Rect;
        }

        return node.Rect;
    }

    private static MachinaRenderOptions ResolveOptions(ResolvedLayoutDocument resolved, MachinaRenderOptions? options)
    {
        if (options is not null)
        {
            return options;
        }

        var rootRect = resolved.Nodes[resolved.RootId].Rect;
        var width = (int)Math.Ceiling(rootRect.Width);
        var height = (int)Math.Ceiling(rootRect.Height);
        return new MachinaRenderOptions(width, height);
    }

    private static void ValidateOptions(MachinaRenderOptions options)
    {
        if (options.Width <= 0)
        {
            throw new InvalidOperationException("Render width must be greater than zero.");
        }

        if (options.Height <= 0)
        {
            throw new InvalidOperationException("Render height must be greater than zero.");
        }
    }
}
