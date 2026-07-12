using Machina.Core.Actions;
using Machina.Core.Semantics;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Runtime.Input;

public sealed class UiHitTestIndex
{
    private readonly IReadOnlyList<Candidate> candidates;

    private UiHitTestIndex(IReadOnlyList<Candidate> candidates)
    {
        this.candidates = candidates;
    }

    public static UiHitTestIndex Build(
        ResolvedLayoutDocument resolved,
        IReadOnlyDictionary<NodeId, UiAction> actions,
        IReadOnlyDictionary<NodeId, UiSemantics>? semantics = null)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(actions);

        var candidates = new List<Candidate>();

        foreach (var nodeId in EnumeratePreOrder(resolved))
        {
            if (!actions.TryGetValue(nodeId, out var action))
            {
                continue;
            }

            var node = resolved.Nodes[nodeId];
            if (node.Rect.Width <= 0 || node.Rect.Height <= 0)
            {
                continue;
            }

            var semantic = semantics is not null && semantics.TryGetValue(nodeId, out var value)
                ? value
                : null;

            candidates.Add(new Candidate(nodeId, node.Rect, action, semantic));
        }

        return new UiHitTestIndex(candidates);
    }

    public UiHitTestResult? HitTest(PointerPoint point)
    {
        for (var i = this.candidates.Count - 1; i >= 0; i--)
        {
            var candidate = this.candidates[i];
            if (Contains(candidate.Rect, point))
            {
                return new UiHitTestResult(candidate.NodeId, candidate.Rect, candidate.Action, candidate.Semantics);
            }
        }

        return null;
    }

    private static bool Contains(Rect rect, PointerPoint point)
    {
        return point.X >= rect.X
            && point.X < rect.X + rect.Width
            && point.Y >= rect.Y
            && point.Y < rect.Y + rect.Height;
    }

    private static IEnumerable<NodeId> EnumeratePreOrder(ResolvedLayoutDocument resolved)
    {
        var stack = new Stack<NodeId>();
        stack.Push(resolved.RootId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            if (!resolved.Children.TryGetValue(current, out var children) || children.Count == 0)
            {
                continue;
            }

            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }

    private sealed record Candidate(
        NodeId NodeId,
        Rect Rect,
        UiAction Action,
        UiSemantics? Semantics);
}
