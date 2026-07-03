using Machina.Layout.Frames;

namespace Machina.Core.Nodes;

public sealed record GridNode(
    IReadOnlyList<GridTrack> Columns,
    IReadOnlyList<GridTrack> Rows,
    IReadOnlyList<UiGridCell> Cells,
    double ColumnGap = 0,
    double RowGap = 0) : UiNode;
