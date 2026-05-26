using Machina.Core.Lowering;
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
        }

        return new UiLoweringResult(rows, styles, textStyles, semantics, actions);
    }
}
