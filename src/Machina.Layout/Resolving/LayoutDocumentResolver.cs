using Machina.Layout.Diagnostics;
using Machina.Layout.Documents;
using Machina.Layout.Frames;
using Machina.Layout.Geometry;
using Machina.Layout.Rows;

namespace Machina.Layout.Resolving;

public static class LayoutDocumentResolver
{
    public static ResolvedLayoutDocument ResolveLayoutDocument(LayoutDocument document, Rect rootRect)
    {
        ArgumentNullException.ThrowIfNull(document); ValidateRootRect(rootRect);
        if (!document.Nodes.ContainsKey(document.RootId)) throw new LayoutError("MissingRootNode", $"Root node '{document.RootId}' does not exist in document nodes.");
        ValidateChildrenEntries(document);
        var resolvedNodes = new Dictionary<NodeId, ResolvedLayoutNode>(document.Nodes.Count); var visitState = new Dictionary<NodeId, VisitState>(document.Nodes.Count);
        ResolveNode(document.RootId, rootRect, document, resolvedNodes, visitState);
        if (resolvedNodes.Count != document.Nodes.Count) { var unreachable = document.Nodes.Keys.First(id => !resolvedNodes.ContainsKey(id)); throw new LayoutError("UnreachableNode", $"Node '{unreachable}' is not reachable from root '{document.RootId}'."); }
        return new ResolvedLayoutDocument(document.RootId, resolvedNodes, new Dictionary<NodeId, IReadOnlyList<NodeId>>(document.Children));
    }

    private static void ResolveNode(NodeId nodeId, Rect resolvedRect, LayoutDocument document, IDictionary<NodeId, ResolvedLayoutNode> resolvedNodes, IDictionary<NodeId, VisitState> visitState)
    {
        if (visitState.TryGetValue(nodeId, out var st)) { if (st == VisitState.Visiting) throw new LayoutError("DocumentCycleDetected", $"Cycle detected while resolving node '{nodeId}'."); if (st == VisitState.Visited) return; }
        visitState[nodeId] = VisitState.Visiting; var node = document.Nodes[nodeId];
        resolvedNodes[nodeId] = new ResolvedLayoutNode(node.Id, resolvedRect, node.Frame, node.Order, node.Z, node.View, node.Slot, node.DebugLabel, node.Layer, node.Arrange);
        var childIds = document.Children[nodeId];
        var childRects = node.Arrange is null ? ResolveDirectChildren(resolvedRect, childIds, document) : ResolveArrangedChildren(node.Arrange, resolvedRect, childIds, document, nodeId);
        for (int i = 0; i < childIds.Count; i++) ResolveNode(childIds[i], childRects[i], document, resolvedNodes, visitState);
        visitState[nodeId] = VisitState.Visited;
    }

    private static List<Rect> ResolveDirectChildren(Rect parentRect, IReadOnlyList<NodeId> childIds, LayoutDocument document)
    {
        var rects = new List<Rect>(childIds.Count);
        foreach (var childId in childIds) { var childNode = document.Nodes[childId]; rects.Add(FrameResolver.ResolveFrame(parentRect, childNode.Frame)); }
        return rects;
    }

    private static List<Rect> ResolveArrangedChildren(ArrangeSpec arrange, Rect parentRect, IReadOnlyList<NodeId> childIds, LayoutDocument document, NodeId nodeId)
    {
        return arrange switch
        {
            StackArrange stack => ResolveStackChildren(stack, parentRect, childIds, document, nodeId),
            GridArrange grid => ResolveGridChildren(grid, parentRect, childIds, document, nodeId),
            _ => throw new LayoutError("UnsupportedArrange", $"Unsupported arrange type '{arrange.GetType().Name}' on node '{nodeId}'.")
        };
    }

    private static List<Rect> ResolveGridChildren(GridArrange grid, Rect parentRect, IReadOnlyList<NodeId> childIds, LayoutDocument document, NodeId nodeId)
    {
        if (grid.Columns.Count == 0) throw new LayoutError("InvalidGridColumns", "Grid columns must contain at least one track.");
        if (grid.Rows.Count == 0) throw new LayoutError("InvalidGridRows", "Grid rows must contain at least one track.");
        ValidateFiniteNonNegative(grid.ColumnGap, "InvalidGridGap", "Grid column gap must be finite and non-negative.");
        ValidateFiniteNonNegative(grid.RowGap, "InvalidGridGap", "Grid row gap must be finite and non-negative.");
        var p = grid.Padding ?? default;
        ValidateFiniteNonNegative(p.Top, "InvalidGridPadding", "Grid padding top invalid."); ValidateFiniteNonNegative(p.Right, "InvalidGridPadding", "Grid padding right invalid."); ValidateFiniteNonNegative(p.Bottom, "InvalidGridPadding", "Grid padding bottom invalid."); ValidateFiniteNonNegative(p.Left, "InvalidGridPadding", "Grid padding left invalid.");
        var contentX = parentRect.X + p.Left; var contentY = parentRect.Y + p.Top; var contentW = parentRect.Width - p.Left - p.Right; var contentH = parentRect.Height - p.Top - p.Bottom;
        if (contentW < 0 || contentH < 0) throw new LayoutError("NegativeGridContentSize", $"Grid content is negative for node '{nodeId}'.");

        var (colSizes, colOffsets) = ResolveTracks(grid.Columns, grid.ColumnGap, contentW, contentX, nodeId);
        var (rowSizes, rowOffsets) = ResolveTracks(grid.Rows, grid.RowGap, contentH, contentY, nodeId);

        var rects = new List<Rect>(childIds.Count);
        foreach (var childId in childIds)
        {
            var frame = document.Nodes[childId].Frame;
            if (frame is not CellFrame cell) throw new LayoutError("InvalidGridChildFrame", $"Node '{childId}' under grid parent '{nodeId}' must use CellFrame, found '{frame.GetType().Name}'.");
            if (cell.Column < 0 || cell.Row < 0 || cell.ColumnSpan <= 0 || cell.RowSpan <= 0) throw new LayoutError("InvalidCellFrame", $"Node '{childId}' has invalid CellFrame values.");
            if (cell.Column + cell.ColumnSpan > grid.Columns.Count || cell.Row + cell.RowSpan > grid.Rows.Count) throw new LayoutError("GridCellOutOfRange", $"Node '{childId}' cell is out of grid bounds.");
            var x = colOffsets[cell.Column]; var y = rowOffsets[cell.Row];
            var w = 0.0; for (var c = cell.Column; c < cell.Column + cell.ColumnSpan; c++) w += colSizes[c]; w += grid.ColumnGap * (cell.ColumnSpan - 1);
            var h = 0.0; for (var r = cell.Row; r < cell.Row + cell.RowSpan; r++) h += rowSizes[r]; h += grid.RowGap * (cell.RowSpan - 1);
            rects.Add(new Rect(x, y, w, h));
        }

        return rects;
    }

    private static (double[] sizes, double[] offsets) ResolveTracks(IReadOnlyList<GridTrack> tracks, double gap, double contentSize, double contentStart, NodeId nodeId)
    {
        var sizes = new double[tracks.Count];
        var gapSum = gap * Math.Max(0, tracks.Count - 1);
        var fixedSum = 0.0;
        var totalWeight = 0.0;
        for (var i = 0; i < tracks.Count; i++)
        {
            switch (tracks[i])
            {
                case FixedGridTrack fixedTrack:
                    if (double.IsNaN(fixedTrack.Size) || double.IsInfinity(fixedTrack.Size) || fixedTrack.Size < 0) throw new LayoutError("InvalidGridTrackSize", "Fixed grid track size must be finite and non-negative.");
                    sizes[i] = fixedTrack.Size; fixedSum += fixedTrack.Size; break;
                case FillGridTrack fillTrack:
                    if (double.IsNaN(fillTrack.Weight) || double.IsInfinity(fillTrack.Weight) || fillTrack.Weight <= 0) throw new LayoutError("InvalidGridTrackWeight", "Fill grid track weight must be finite and greater than zero.");
                    totalWeight += fillTrack.Weight; break;
                default:
                    throw new LayoutError("InvalidGridTrackSize", $"Unsupported grid track type: {tracks[i].GetType().Name}");
            }
        }

        var remaining = contentSize - fixedSum - gapSum;
        if (remaining < 0) throw new LayoutError("NegativeGridRemainingSpace", $"Grid remaining space is negative for node '{nodeId}'.");
        if (totalWeight > 0)
        {
            for (var i = 0; i < tracks.Count; i++) if (tracks[i] is FillGridTrack fillTrack) sizes[i] = remaining * (fillTrack.Weight / totalWeight);
        }

        var offsets = new double[tracks.Count];
        var cursor = contentStart;
        for (var i = 0; i < tracks.Count; i++) { offsets[i] = cursor; cursor += sizes[i] + gap; }
        return (sizes, offsets);
    }

    private static List<Rect> ResolveStackChildren(StackArrange stack, Rect parentRect, IReadOnlyList<NodeId> childIds, LayoutDocument document, NodeId nodeId)
    {
        ValidateFiniteNonNegative(stack.Gap, "InvalidStackGap", "Stack gap must be finite and non-negative.");
        var p = stack.Padding ?? default;
        ValidateFiniteNonNegative(p.Top, "InvalidStackPadding", "Stack padding top invalid."); ValidateFiniteNonNegative(p.Right, "InvalidStackPadding", "Stack padding right invalid."); ValidateFiniteNonNegative(p.Bottom, "InvalidStackPadding", "Stack padding bottom invalid."); ValidateFiniteNonNegative(p.Left, "InvalidStackPadding", "Stack padding left invalid.");
        var contentX = parentRect.X + p.Left; var contentY = parentRect.Y + p.Top; var contentW = parentRect.Width - p.Left - p.Right; var contentH = parentRect.Height - p.Top - p.Bottom;
        if (contentW < 0 || contentH < 0) throw new LayoutError("NegativeStackContentSize", $"Stack content is negative for node '{nodeId}'.");
        if (childIds.Count == 0) return new List<Rect>();
        var infos = new List<(FrameSpec frame, double fixedMain, double cross, double weight, bool fillCross)>(childIds.Count);
        foreach (var cid in childIds)
        {
            var frame = document.Nodes[cid].Frame; if (frame is FixedFrame f) { ValidateFiniteNonNegative(f.Width, "InvalidFixedFrameSize", "Fixed width invalid."); ValidateFiniteNonNegative(f.Height, "InvalidFixedFrameSize", "Fixed height invalid."); infos.Add((f, stack.Axis == StackAxis.Horizontal ? f.Width : f.Height, stack.Axis == StackAxis.Horizontal ? f.Height : f.Width, 0, false)); }
            else if (frame is FillFrame ff) { ValidateFinitePositive(ff.Weight, "InvalidFillWeight", "Fill weight invalid."); if (ff.Cross is { } c) ValidateFiniteNonNegative(c, "InvalidFillCross", "Fill cross invalid."); infos.Add((ff, 0, ff.Cross ?? (stack.Axis == StackAxis.Horizontal ? contentH : contentW), ff.Weight, ff.Cross is null || ff.CrossFill)); }
            else throw new LayoutError("InvalidStackChildFrame", $"Node '{cid}' under stack parent '{nodeId}' must use FixedFrame or FillFrame, found '{frame.GetType().Name}'.");
        }
        var mainSize = stack.Axis == StackAxis.Horizontal ? contentW : contentH; var crossSize = stack.Axis == StackAxis.Horizontal ? contentH : contentW;
        var fixedMain = infos.Where(i => i.frame is FixedFrame).Sum(i => i.fixedMain); var fills = infos.Count(i => i.frame is FillFrame); var gapCount = Math.Max(0, childIds.Count - 1); var gapSum = stack.Gap * gapCount; var remaining = mainSize - fixedMain - gapSum;
        if (remaining < 0) throw new LayoutError("NegativeStackRemainingSpace", $"Stack remaining main-axis space is negative for node '{nodeId}'.");
        var start = 0.0; var gap = stack.Gap;
        if (fills == 0) { var free = remaining; if (stack.Justify == StackJustify.Center) start = free / 2; else if (stack.Justify == StackJustify.End) start = free; else if (stack.Justify == StackJustify.SpaceBetween) { if (childIds.Count <= 1) { start = 0; gap = 0; } else gap = stack.Gap + free / (childIds.Count - 1); } }
        var totalWeight = infos.Sum(i => i.weight); var cursor = start; var rects = new List<Rect>(childIds.Count);
        foreach (var i in infos)
        {
            var childMain = i.frame is FillFrame ? remaining * (i.weight / totalWeight) : i.fixedMain; var childCross = i.cross; var crossOffset = stack.Align switch { StackAlign.Start => 0, StackAlign.Center => (crossSize - childCross) / 2, StackAlign.End => crossSize - childCross, _ => 0 };
            rects.Add(stack.Axis == StackAxis.Horizontal ? new Rect(contentX + cursor, contentY + crossOffset, childMain, childCross) : new Rect(contentX + crossOffset, contentY + cursor, childCross, childMain)); cursor += childMain + gap;
        }
        return rects;
    }

    private static void ValidateFinitePositive(double value, string code, string msg) { if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0) throw new LayoutError(code, msg); }
    private static void ValidateFiniteNonNegative(double value, string code, string msg) { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new LayoutError(code, msg); }
    private static void ValidateRootRect(Rect rootRect) { ValidateFinite(rootRect.X, nameof(rootRect.X)); ValidateFinite(rootRect.Y, nameof(rootRect.Y)); ValidateFinite(rootRect.Width, nameof(rootRect.Width)); ValidateFinite(rootRect.Height, nameof(rootRect.Height)); if (rootRect.Width < 0 || rootRect.Height < 0) throw new LayoutError("InvalidRootRect", "Root rect width and height must be non-negative."); }
    private static void ValidateChildrenEntries(LayoutDocument document) { if (!document.Children.ContainsKey(document.RootId)) throw new LayoutError("MissingChildrenEntry", $"Root node '{document.RootId}' is missing a children entry."); foreach (var id in document.Nodes.Keys) if (!document.Children.ContainsKey(id)) throw new LayoutError("MissingChildrenEntry", $"Node '{id}' is missing a children entry."); foreach (var (parent, children) in document.Children) { if (!document.Nodes.ContainsKey(parent)) throw new LayoutError("UnknownChildNode", $"Children map contains unknown parent '{parent}'."); foreach (var child in children) if (!document.Nodes.ContainsKey(child)) throw new LayoutError("UnknownChildNode", $"Children entry for '{parent}' references unknown child '{child}'."); } }
    private static void ValidateFinite(double value, string field) { if (double.IsNaN(value) || double.IsInfinity(value)) throw new LayoutError("InvalidRootRect", $"Root rect field '{field}' must be finite."); }
    private enum VisitState { Visiting, Visited }
}
