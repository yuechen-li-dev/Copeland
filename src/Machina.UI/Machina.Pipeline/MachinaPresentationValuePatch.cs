using Machina.Core.Lowering;
using Machina.Core.Semantics;
using Machina.Core.Styling;
using Machina.Layout.Rows;
using Machina.Presentation;

namespace Machina.Pipeline;

/// <summary>
/// A bounded update to values whose nodes and geometry already exist in a prepared presentation.
/// This is not a tree diff: adding or removing operations requires normal preparation.
/// </summary>
public sealed class MachinaPresentationValuePatch
{
    private readonly Dictionary<NodeId, string> text = [];
    private readonly Dictionary<NodeId, UiStyle> styles = [];
    private readonly Dictionary<NodeId, UiSemantics> semantics = [];

    public IReadOnlyDictionary<NodeId, string> Text => text;

    public IReadOnlyDictionary<NodeId, UiStyle> Styles => styles;

    public IReadOnlyDictionary<NodeId, UiSemantics> Semantics => semantics;

    public void SetText(NodeId nodeId, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Patched presentation text must not be empty or whitespace.", nameof(value));
        }

        // The caller must classify the change as value-only. Text whose measured
        // bounds affect layout belongs on the normal layout invalidation path.
        text[nodeId] = value;
    }

    public void SetStyle(NodeId nodeId, UiStyle value)
    {
        styles[nodeId] = value ?? throw new ArgumentNullException(nameof(value));
    }

    public void SetSemantics(NodeId nodeId, UiSemantics value)
    {
        semantics[nodeId] = value ?? throw new ArgumentNullException(nameof(value));
    }
}

public static class MachinaPreparedPresentationUpdater
{
    public static MachinaPreparedPresentation ApplyValues(
        MachinaPreparedPresentation prepared,
        MachinaPresentationValuePatch patch)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(patch);

        Dictionary<NodeId, UiStyle> styles = Copy(prepared.Lowering.Styles);
        Dictionary<NodeId, UiSemantics> semantics = Copy(prepared.Lowering.Semantics);

        ApplyStyles(styles, patch.Styles);
        ApplySemantics(semantics, patch.Semantics);
        ApplyTextSemantics(semantics, patch.Text);

        MachinaPresentationOperation[] operations = prepared.PresentationFrame.Operations.ToArray();
        var patchedText = new HashSet<NodeId>();
        var patchedFills = new HashSet<NodeId>();
        var patchedStrokes = new HashSet<NodeId>();

        for (int index = 0; index < operations.Length; index++)
        {
            switch (operations[index])
            {
                case PositionedTextOperation positioned:
                    var textNodeId = new NodeId(positioned.SourceId);
                    if (patch.Text.TryGetValue(textNodeId, out string? text))
                    {
                        operations[index] = new PositionedTextOperation(
                            positioned.SourceId,
                            positioned.Rect,
                            text,
                            positioned.Style,
                            positioned.Color);
                        patchedText.Add(textNodeId);
                    }
                    break;

                case FillRectangleOperation fill:
                    var fillNodeId = new NodeId(fill.SourceId);
                    if (patch.Styles.TryGetValue(fillNodeId, out UiStyle? fillStyle))
                    {
                        if (fillStyle.Background is not ColorToken background)
                        {
                            throw RequiresRebuild(fillNodeId, "fill operation shape changed");
                        }
                        operations[index] = new FillRectangleOperation(fill.SourceId, fill.Rect, background);
                        patchedFills.Add(fillNodeId);
                    }
                    break;

                case StrokeRectangleOperation stroke:
                    var strokeNodeId = new NodeId(stroke.SourceId);
                    if (patch.Styles.TryGetValue(strokeNodeId, out UiStyle? strokeStyle))
                    {
                        if (strokeStyle.BorderColor is not ColorToken borderColor || strokeStyle.BorderThickness <= 0)
                        {
                            throw RequiresRebuild(strokeNodeId, "stroke operation shape changed");
                        }
                        operations[index] = new StrokeRectangleOperation(
                            stroke.SourceId,
                            stroke.Rect,
                            borderColor,
                            strokeStyle.BorderThickness);
                        patchedStrokes.Add(strokeNodeId);
                    }
                    break;
            }
        }

        ValidatePatchedText(patch.Text.Keys, patchedText);
        ValidatePatchedStyles(prepared.Lowering.Styles, patch.Styles, patchedFills, patchedStrokes);

        var lowering = new UiLoweringResult(
            prepared.Lowering.Rows,
            styles,
            prepared.Lowering.TextStyles,
            semantics,
            prepared.Lowering.Actions,
            prepared.Lowering.NodePayloads);
        var frame = new MachinaPresentationFrame(prepared.PresentationFrame.Viewport, operations);

        return new MachinaPreparedPresentation(
            lowering,
            prepared.Document,
            prepared.Resolved,
            prepared.HitTest.WithSemantics(semantics),
            frame);
    }

    private static Dictionary<TKey, TValue> Copy<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> source)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>(source.Count);
        foreach ((TKey key, TValue value) in source)
        {
            result.Add(key, value);
        }
        return result;
    }

    private static void ApplyStyles(
        IDictionary<NodeId, UiStyle> target,
        IReadOnlyDictionary<NodeId, UiStyle> updates)
    {
        foreach ((NodeId nodeId, UiStyle style) in updates)
        {
            if (!target.ContainsKey(nodeId))
            {
                throw RequiresRebuild(nodeId, "style node does not exist");
            }
            target[nodeId] = style;
        }
    }

    private static void ApplySemantics(
        IDictionary<NodeId, UiSemantics> target,
        IReadOnlyDictionary<NodeId, UiSemantics> updates)
    {
        foreach ((NodeId nodeId, UiSemantics semantic) in updates)
        {
            if (!target.ContainsKey(nodeId))
            {
                throw RequiresRebuild(nodeId, "semantic node does not exist");
            }
            target[nodeId] = semantic;
        }
    }

    private static void ApplyTextSemantics(
        IDictionary<NodeId, UiSemantics> target,
        IReadOnlyDictionary<NodeId, string> updates)
    {
        foreach ((NodeId nodeId, string text) in updates)
        {
            if (!target.TryGetValue(nodeId, out UiSemantics? semantic)
                || semantic.Role is not (UiRole.Text or UiRole.Label))
            {
                throw RequiresRebuild(nodeId, "text semantic node does not exist");
            }
            target[nodeId] = semantic with { Label = text };
        }
    }

    private static void ValidatePatchedText(
        IEnumerable<NodeId> requested,
        IReadOnlySet<NodeId> patched)
    {
        foreach (NodeId nodeId in requested)
        {
            if (!patched.Contains(nodeId))
            {
                throw RequiresRebuild(nodeId, "text operation does not exist");
            }
        }
    }

    private static void ValidatePatchedStyles(
        IReadOnlyDictionary<NodeId, UiStyle> previous,
        IReadOnlyDictionary<NodeId, UiStyle> updates,
        IReadOnlySet<NodeId> patchedFills,
        IReadOnlySet<NodeId> patchedStrokes)
    {
        foreach ((NodeId nodeId, UiStyle style) in updates)
        {
            UiStyle oldStyle = previous[nodeId];
            bool oldFill = oldStyle.Background is not null;
            bool newFill = style.Background is not null;
            bool oldStroke = oldStyle.BorderColor is not null && oldStyle.BorderThickness > 0;
            bool newStroke = style.BorderColor is not null && style.BorderThickness > 0;

            if (oldFill != newFill || oldStroke != newStroke)
            {
                throw RequiresRebuild(nodeId, "presentation operation shape changed");
            }
            if (newFill && !patchedFills.Contains(nodeId))
            {
                throw RequiresRebuild(nodeId, "fill operation does not exist");
            }
            if (newStroke && !patchedStrokes.Contains(nodeId))
            {
                throw RequiresRebuild(nodeId, "stroke operation does not exist");
            }
        }
    }

    private static InvalidOperationException RequiresRebuild(NodeId nodeId, string reason)
    {
        return new InvalidOperationException(
            $"Machina value patch for '{nodeId.Value}' requires a layout or topology rebuild because the {reason}.");
    }
}
