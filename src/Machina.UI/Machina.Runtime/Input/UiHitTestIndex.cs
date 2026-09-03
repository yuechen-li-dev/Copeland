using Machina.Core.Actions;
using Machina.Core.Semantics;
using Machina.Layout.Documents;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Runtime.Input;

public sealed class UiHitTestIndex
{
    private readonly IReadOnlyList<Candidate> candidates;
    private readonly IReadOnlyDictionary<NodeId, UiSemantics>? semantics;

    private UiHitTestIndex(
        IReadOnlyList<Candidate> candidates,
        IReadOnlyDictionary<NodeId, UiSemantics>? semantics)
    {
        this.candidates = candidates;
        this.semantics = semantics;
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

            candidates.Add(new Candidate(nodeId, node.Rect, action));
        }

        return new UiHitTestIndex(candidates, semantics);
    }

    public UiHitTestIndex WithSemantics(IReadOnlyDictionary<NodeId, UiSemantics> updatedSemantics)
    {
        ArgumentNullException.ThrowIfNull(updatedSemantics);
        return new UiHitTestIndex(candidates, updatedSemantics);
    }

    public UiHitTestResult? HitTest(PointerPoint point)
    {
        for (var i = this.candidates.Count - 1; i >= 0; i--)
        {
            var candidate = this.candidates[i];
            if (Contains(candidate.Rect, point))
            {
                UiSemantics? semantic = semantics is not null
                    && semantics.TryGetValue(candidate.NodeId, out UiSemantics? value)
                    ? value
                    : null;
                return new UiHitTestResult(candidate.NodeId, candidate.Rect, candidate.Action, semantic);
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
        UiAction Action);
}
