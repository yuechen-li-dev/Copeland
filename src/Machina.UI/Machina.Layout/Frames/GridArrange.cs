namespace Machina.Layout.Frames;

public sealed record GridArrange(
    IReadOnlyList<GridTrack> Columns,
    IReadOnlyList<GridTrack> Rows,
    double ColumnGap = 0,
    double RowGap = 0,
    EdgeInsets? Padding = null) : ArrangeSpec;
