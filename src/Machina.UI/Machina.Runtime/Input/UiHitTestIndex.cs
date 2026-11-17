using Machina.Core.Actions;
using Machina.Core.Semantics;
using Machina.Core.Styling;
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
        return Build(resolved, actions, semantics, null);
    }

    public static UiHitTestIndex Build(
        ResolvedLayoutDocument resolved,
        IReadOnlyDictionary<NodeId, UiAction> actions,
        IReadOnlyDictionary<NodeId, UiSemantics>? semantics,
        IReadOnlyDictionary<NodeId, UiStyle>? styles)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(actions);

        var candidates = new List<Candidate>();
        var clips = new Dictionary<NodeId, Rect?>();
        var pending = new Stack<(NodeId Id, Rect? Clip)>();
        pending.Push((resolved.RootId, null));
        while (pending.TryPop(out var next))
        {
            Rect? clip = next.Clip;
            if (styles?.GetValueOrDefault(next.Id)?.ClipToBounds == true)
                clip = clip is { } inherited ? Intersect(inherited, resolved.Nodes[next.Id].Rect) : resolved.Nodes[next.Id].Rect;
            clips[next.Id] = clip;
            if (resolved.Children.TryGetValue(next.Id, out var children))
                foreach (var child in children) pending.Push((child, clip));
        }

        foreach (var nodeId in EnumeratePreOrder(resolved))
        {
            if (!actions.TryGetValue(nodeId, out var action))
            {
                continue;
            }

            var node = resolved.Nodes[nodeId];
            Rect bounds = clips[nodeId] is { } clipBounds ? Intersect(node.Rect, clipBounds) : node.Rect;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            candidates.Add(new Candidate(nodeId, bounds, action));
        }

        return new UiHitTestIndex(candidates, semantics);
    }

    public UiHitTestIndex WithSemantics(IReadOnlyDictionary<NodeId, UiSemantics> updatedSemantics)
    {
        ArgumentNullException.ThrowIfNull(updatedSemantics);
        return new UiHitTestIndex(candidates, updatedSemantics);
    }

    public bool Contains(NodeId id) => candidates.Any(candidate => candidate.NodeId == id);

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

    private static Rect Intersect(Rect a, Rect b)
    {
        double left = Math.Max(a.X, b.X), top = Math.Max(a.Y, b.Y);
        return new Rect(left, top, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - left),
            Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - top));
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
