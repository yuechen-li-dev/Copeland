using Machina.Core.Lowering;
using Machina.Core.Actions;
using Machina.Layout.Rows;
using Machina.Layout.Documents;
using Machina.Presentation;
using Machina.Runtime.Input;

namespace Machina.Pipeline;

/// <summary>
/// Immutable Machina-owned preparation result. Raster realization is intentionally external.
/// </summary>
public sealed record MachinaPreparedPresentation(
    UiLoweringResult Lowering,
    LayoutDocument Document,
    ResolvedLayoutDocument Resolved,
    UiHitTestIndex HitTest,
    MachinaPresentationFrame PresentationFrame)
{
    public bool TryGetActionCenter(UiAction action, out PointerPoint point)
    {
        ArgumentNullException.ThrowIfNull(action);
        return TryGetActionCenter(candidate => candidate == action, false, out point);
    }

    /// <summary>External diagnostic lookup. Ambiguous aliases never select an arbitrary message.</summary>
    public bool TryGetActionCenter(UiActionId id, out PointerPoint point)
    {
        return TryGetActionCenter(candidate => candidate.Id == id, true, out point);
    }

    private bool TryGetActionCenter(Func<UiAction, bool> matches, bool requireUniqueMessage, out PointerPoint point)
    {
        NodeId? firstNode = null;
        UiAction? firstAction = null;
        foreach (var pair in Lowering.Actions)
        {
            if (!matches(pair.Value) || Lowering.Semantics.GetValueOrDefault(pair.Key)?.Disabled == true)
            {
                continue;
            }
            if (requireUniqueMessage && firstAction is not null && firstAction != pair.Value)
            {
                point = default;
                return false;
            }
            firstNode ??= pair.Key;
            firstAction = pair.Value;
        }
        if (firstNode is { } node)
        {
            var rect = Resolved.Nodes[node].Rect;
            point = new PointerPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            return true;
        }
        point = default;
        return false;
    }
}
