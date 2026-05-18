namespace Machina.Layout.Frames;

public sealed record CellFrame(
    int Column,
    int Row,
    int ColumnSpan = 1,
    int RowSpan = 1) : FrameSpec;
