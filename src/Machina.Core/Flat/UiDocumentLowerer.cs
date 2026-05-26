using Machina.Core.Lowering;
using Machina.Layout.Frames;
using Machina.Layout.Rows;

namespace Machina.Core.Flat;

public static class UiDocumentLowerer
{
    public static UiLoweringResult Lower(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var rows = new List<LayoutRow>(document.Rows.Count);
        var styles = new Dictionary<NodeId, Machina.Core.Styling.UiStyle>();
        var textStyles = new Dictionary<NodeId, Machina.Core.Styling.TextStyle>();
        var semantics = new Dictionary<NodeId, Machina.Core.Semantics.UiSemantics>();
        var actions = new Dictionary<NodeId, Machina.Core.Actions.UiAction>();

        foreach (var row in document.Rows)
        {
            if (row.View is not null && row.Component is not null)
            {
                throw new UiLoweringError(
                    "InvalidFlatRowHost",
                    $"Row '{row.Id}' cannot define both view and component.");
            }

            rows.Add(new LayoutRow(row.Id, row.Frame, row.Parent, row.Order, Arrange: row.Arrange));

            if (row.View?.Style is not null)
            {
                styles[row.Id] = row.View.Style;
            }

            if (row.View?.TextStyle is not null)
            {
                textStyles[row.Id] = row.View.TextStyle;
            }

            if (row.View?.Semantics is not null)
            {
                semantics[row.Id] = row.View.Semantics;
            }

            if (row.View?.Action is not null)
            {
                actions[row.Id] = row.View.Action;
            }

            if (row.Component is not null)
            {
                LowerHostedComponent(row, rows, styles, textStyles, semantics, actions);
            }
        }

        return new UiLoweringResult(rows, styles, textStyles, semantics, actions);
    }

    private static void LowerHostedComponent(
        UiRow host,
        List<LayoutRow> rows,
        Dictionary<NodeId, Machina.Core.Styling.UiStyle> styles,
        Dictionary<NodeId, Machina.Core.Styling.TextStyle> textStyles,
        Dictionary<NodeId, Machina.Core.Semantics.UiSemantics> semantics,
        Dictionary<NodeId, Machina.Core.Actions.UiAction> actions)
    {
        var componentResult = UiLowerer.Lower(host.Component!);
        var scopedIds = BuildScopedIdMap(host.Id, componentResult.Rows);
        var componentRootId = componentResult.Rows.Single(x => x.Parent is null).Id;

        foreach (var row in componentResult.Rows)
        {
            var scopedId = scopedIds[row.Id];
            var parent = row.Parent is null ? host.Id : scopedIds[row.Parent.Value];
            var frame = row.Id == componentRootId ? new AnchorFrame(Left: 0, Right: 0, Top: 0, Bottom: 0) : row.Frame;
            rows.Add(new LayoutRow(scopedId, frame, parent, row.Order, row.Z, row.View, row.Slot, row.DebugLabel, row.Layer, row.Arrange));
        }

        CopyMetadata(componentResult.Styles, scopedIds, styles);
        CopyMetadata(componentResult.TextStyles, scopedIds, textStyles);
        CopyMetadata(componentResult.Semantics, scopedIds, semantics);
        CopyMetadata(componentResult.Actions, scopedIds, actions);
    }

    private static Dictionary<NodeId, NodeId> BuildScopedIdMap(NodeId hostId, IReadOnlyList<LayoutRow> componentRows)
    {
        var scopedIds = new Dictionary<NodeId, NodeId>(componentRows.Count);
        foreach (var row in componentRows)
        {
            scopedIds[row.Id] = new NodeId($"{hostId.Value}/{row.Id.Value}");
        }

        return scopedIds;
    }

    private static void CopyMetadata<T>(
        IReadOnlyDictionary<NodeId, T> source,
        IReadOnlyDictionary<NodeId, NodeId> scopedIds,
        IDictionary<NodeId, T> destination)
    {
        foreach (var pair in source)
        {
            destination[scopedIds[pair.Key]] = pair.Value;
        }
    }
}
