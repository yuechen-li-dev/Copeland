using System.Globalization;
using System.Text;
using Machina.Layout.Rows;

namespace Machina.Pipeline;

/// <summary>
/// A deterministic text rendering of a prepared frame's resolved layout: one line per node, indented by tree depth,
/// in paint order. Each line has the node ID, its rect (x,y wxh), and when present: clip, role and label, action,
/// disabled and focusable flags. Use it in tests (golden comparisons) and in reviews, where reading geometry as
/// text is cheaper and more exact than reading a screenshot.
/// </summary>
public static class MachinaLayoutDump
{
    /// <param name="compact">Omit generated anonymous nodes (IDs starting "ui_") that carry no semantics or action;
    /// their children are kept at the parent's depth.</param>
    /// <param name="maxNodes">Stops after this many lines and appends a truncation marker.</param>
    public static string Format(MachinaPreparedPresentation frame, bool compact = true, int maxNodes = 4000)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var resolved = frame.Resolved;
        var lowering = frame.Lowering;
        var text = new StringBuilder();
        int lines = 0;
        var stack = new Stack<(NodeId Id, int Depth)>();
        stack.Push((resolved.RootId, 0));
        while (stack.TryPop(out var item))
        {
            var (id, depth) = item;
            bool anonymous = id.Value.StartsWith("ui_", StringComparison.Ordinal)
                && !lowering.Semantics.ContainsKey(id) && !lowering.Actions.ContainsKey(id);
            bool shown = !(compact && anonymous && id != resolved.RootId);
            if (shown)
            {
                if (lines++ >= maxNodes) { text.Append("... truncated at ").Append(maxNodes).Append(" nodes\n"); break; }
                text.Append(' ', depth * 2).Append(id.Value).Append(' ').Append(Rect(resolved.Nodes[id].Rect));
                if (lowering.Styles.TryGetValue(id, out var style) && style.ClipToBounds) text.Append(" clip");
                if (lowering.Semantics.TryGetValue(id, out var semantics))
                {
                    text.Append(" role=").Append(semantics.Role);
                    if (!string.IsNullOrEmpty(semantics.Label)) text.Append(" \"").Append(semantics.Label.Replace("\"", "\\\"")).Append('"');
                    if (semantics.Disabled) text.Append(" disabled");
                    if (semantics.Focusable) text.Append(" focusable");
                }
                if (lowering.Actions.TryGetValue(id, out var action)) text.Append(" action=").Append(action.Name);
                text.Append('\n');
            }
            if (!resolved.Children.TryGetValue(id, out var children)) continue;
            int childDepth = shown ? depth + 1 : depth;
            for (int index = children.Count - 1; index >= 0; index--) stack.Push((children[index], childDepth));
        }
        return text.ToString();
    }

    private static string Rect(Machina.Layout.Geometry.Rect rect) => string.Create(CultureInfo.InvariantCulture,
        $"{Number(rect.X)},{Number(rect.Y)} {Number(rect.Width)}x{Number(rect.Height)}");

    private static string Number(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
