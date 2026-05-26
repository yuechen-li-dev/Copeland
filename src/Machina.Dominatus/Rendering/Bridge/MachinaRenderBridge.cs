using Dominatus.Core.Runtime;
using Machina.Core.Lowering;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Dominatus.Rendering.Commands;
using Machina.Layout.Documents;
using Machina.Layout.Projection;
using Machina.Layout.Rows;

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

        foreach (var node in EnumeratePreOrder(resolved))
        {
            EmitFillAndStrokeCommands(node, lowering.Styles, commands);
            EmitTextCommand(node, lowering.TextStyles, lowering.Semantics, commands);
        }

        commands.Add(new EndFrameCommand());
        return commands;
    }

    private static IEnumerable<ResolvedLayoutNode> EnumeratePreOrder(ResolvedLayoutDocument resolved)
    {
        var tree = ResolvedLayoutTreeBuilder.ToResolvedTree(resolved);
        var stack = new Stack<ResolvedLayoutTree>();
        stack.Push(tree);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return new ResolvedLayoutNode(
                current.Id,
                current.Rect,
                current.Frame,
                current.Order,
                current.Z,
                current.View,
                current.Slot,
                current.DebugLabel,
                current.Layer,
                current.Arrange);

            for (var i = current.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(current.Children[i]);
            }
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

    private static void EmitTextCommand(
        ResolvedLayoutNode node,
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

        commands.Add(new DrawTextCommand(node.Id.Value, node.Rect, semantic.Label, style));
    }

    private static bool ShouldDrawText(UiSemantics semantic)
    {
        return semantic.Role == UiRole.Text
            || semantic.Role == UiRole.Label;
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
