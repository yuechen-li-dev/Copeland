using Machina.Core.Actions;
using Machina.Core.Authoring;
using Machina.Core.Nodes;
using Machina.Core.Styling;
using Machina.Standard.Theme;

namespace Machina.Standard.Authoring;

public sealed record UiTableColumn(string Label, double Width, UiAction? SortAction = null);
public sealed record UiTableRow(string Id, IReadOnlyList<string> Cells, UiAction? SelectAction, bool Selected = false);

/// <summary>A bounded visible table window. Hosts own querying and scroll/selection state.</summary>
public static class UiDataTable
{
    public static UiNode Build(string id, IReadOnlyList<UiTableColumn> columns,
        IReadOnlyList<UiTableRow> rows, double headerHeight = 36, StandardColors? colors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (columns.Count == 0 || !double.IsFinite(headerHeight) || headerHeight <= 0
            || columns.Any(column => !double.IsFinite(column.Width) || column.Width <= 0))
        {
            throw new ArgumentException("A table needs columns with positive finite dimensions.", nameof(columns));
        }
        var nodes = new List<UiNode>();
        StandardColors palette = colors ?? StandardTheme.Default.Colors;
        double width = columns.Sum(column => column.Width);
        double x = 0;
        for (int column = 0; column < columns.Count; column++)
        {
            UiTableColumn definition = columns[column];
            nodes.Add(UI.Anchor(Cell(definition.Label, $"{id}.header.{column}",
                definition.SortAction, palette.Secondary, palette, definition.Width, headerHeight),
                left: x, top: 0, width: definition.Width, height: headerHeight));
            x += definition.Width;
        }
        for (int row = 0; row < rows.Count; row++)
        {
            UiTableRow data = rows[row];
            if (data.Cells.Count != columns.Count)
            {
                throw new ArgumentException("Table rows must match the column count.", nameof(rows));
            }
            x = 0;
            ColorToken background = row % 2 == 0 ? palette.Background : palette.Muted;
            if (data.Selected)
            {
                background = palette.Primary;
            }
            for (int column = 0; column < columns.Count; column++)
            {
                nodes.Add(UI.Anchor(Cell(data.Cells[column], $"{id}.row.{data.Id}.{column}",
                    data.SelectAction, background, palette, columns[column].Width, UiTableLayout.RowHeight),
                    left: x, top: headerHeight + row * UiTableLayout.RowHeight,
                    width: columns[column].Width, height: UiTableLayout.RowHeight));
                x += columns[column].Width;
            }
        }
        return UI.Rect(id: id, width: width,
            height: headerHeight + Math.Max(1, rows.Count) * UiTableLayout.RowHeight,
            child: UI.Layer(id: id + ".cells", children: nodes));
    }

    private static UiNode Cell(string text, string id, UiAction? action, ColorToken background,
        StandardColors colors, double width, double height)
    {
        UiNode button = StandardUI.Button(text, id: id, action: action,
            style: new StandardButtonStyle(background, colors.Foreground, colors.Border, 1,
                new TextStyle(Size: TextSize.Md, AlignX: TextAlignX.Left, AlignY: TextAlignY.Center),
                width, height, HorizontalPadding: 8));
        var shell = (RectNode)button;
        return shell with { Style = shell.Style! with { ClipToBounds = true } };
    }
}

